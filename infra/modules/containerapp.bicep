// Container Apps environment (consumption, scale to zero) and the API app. Every secret is a Key Vault reference
// resolved by the user-assigned identity; plain settings are environment variables. Names below are the exact
// configuration keys the API reads (double underscore = ':'); infra/required-settings.txt lists the ones that must stay wired.
param environmentName string
param appName string
param location string
param logAnalyticsName string
param identityId string
param keyVaultUri string
param apiImage string
param registryServer string = 'ghcr.io'
@description('Empty for a public image. Otherwise the password is read from Key Vault secret registry-password.')
param registryUsername string = ''
@minValue(0)
param minReplicas int = 0
@minValue(1)
param maxReplicas int = 2
param ipAllowList array = []

// Public and branding
param brandingDomain string
param viewerUrl string
param publicBaseUrl string
param adminEmails string
param emailSenderAddress string
param contentSafetyEndpoint string
param cloudflareZoneId string
param links object
param legal object
param googleClientIds array
param appleClientId string
param sentryEnabled bool = true

// Abuse and attestation
@description('Must be true at launch. May be false only for first bring-up before the store apps exist.')
param requireAttestation bool = true
param playIntegrityPackageName string = ''
param appAttestAppId string = ''
param trustedProxyHops int = 1

// App review
param reviewMode bool = false
param reviewEmails array = []

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = {
  name: logAnalyticsName
}

resource env 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: environmentName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: workspace.properties.customerId
        sharedKey: workspace.listKeys().primarySharedKey
      }
    }
  }
}

func kvSecret(name string, vaultUri string, identity string) object => {
  name: name
  keyVaultUrl: '${vaultUri}secrets/${name}'
  identity: identity
}

var attestationSecrets = !empty(playIntegrityPackageName) ? [
  'play-integrity-decryption-key'
  'play-integrity-verification-key'
] : []

var secretNames = concat([
  'postgres-connection-string'
  'storage-connection-string'
  'acs-connection-string'
  'appinsights-connection-string'
  'jwt-signing-key'
  'email-code-pepper'
  'ban-pepper'
  'content-safety-key'
  'cloudflare-api-token'
], attestationSecrets, sentryEnabled ? ['sentry-dsn'] : [], reviewMode ? ['review-code'] : [], empty(registryUsername) ? [] : ['registry-password'])

var secrets = [for s in secretNames: kvSecret(s, keyVaultUri, identityId)]

var settings = [
  { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
  { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
  { name: 'ConnectionStrings__Default', secretRef: 'postgres-connection-string' }
  { name: 'Storage__ConnectionString', secretRef: 'storage-connection-string' }
  { name: 'Storage__PublicBaseUrl', value: publicBaseUrl }
  { name: 'DataProtection__PersistToBlob', value: 'true' }
  // EF Core locks the database while migrating, so replicas starting together are safe. Turn off to migrate out of band.
  { name: 'Database__MigrateOnStartup', value: 'true' }
  { name: 'Email__Acs__ConnectionString', secretRef: 'acs-connection-string' }
  { name: 'Email__SenderAddress', value: emailSenderAddress }
  { name: 'Telemetry__AzureMonitorConnectionString', secretRef: 'appinsights-connection-string' }
  { name: 'Auth__Jwt__SigningKey', secretRef: 'jwt-signing-key' }
  { name: 'Auth__EmailCode__Pepper', secretRef: 'email-code-pepper' }
  { name: 'Auth__Apple__ClientId', value: appleClientId }
  { name: 'Admin__Emails', value: adminEmails }
  { name: 'Admin__BanPepper', secretRef: 'ban-pepper' }
  { name: 'ContentSafety__Endpoint', value: contentSafetyEndpoint }
  { name: 'ContentSafety__Key', secretRef: 'content-safety-key' }
  { name: 'Branding__Domain', value: brandingDomain }
  { name: 'Web__ViewerUrl', value: viewerUrl }
  { name: 'Links__AppleTeamId', value: links.appleTeamId }
  { name: 'Links__Sha256Fingerprints', value: join(links.sha256Fingerprints, ',') }
  { name: 'Legal__DmcaAgentName', value: legal.agentName }
  { name: 'Legal__DmcaAgentEmail', value: legal.agentEmail }
  { name: 'Legal__DmcaAgentAddress', value: legal.agentAddress }
  { name: 'Cloudflare__ZoneId', value: cloudflareZoneId }
  { name: 'Cloudflare__ApiToken', secretRef: 'cloudflare-api-token' }
  // Behind the ingress the TCP peer is the proxy; the API reads the connecting address from the last X-Forwarded-For entry.
  { name: 'Abuse__TrustedProxyHops', value: string(trustedProxyHops) }
  { name: 'Abuse__RequireAttestation', value: string(requireAttestation) }
  { name: 'Abuse__PlayIntegrity__PackageName', value: playIntegrityPackageName }
  { name: 'Abuse__AppAttest__AppId', value: appAttestAppId }
  { name: 'Auth__ReviewMode', value: string(reviewMode) }
]

var googleSettings = [for (id, i) in googleClientIds: { name: 'Auth__Google__ClientIds__${i}', value: id }]
var reviewEmailSettings = [for (mail, i) in reviewEmails: { name: 'Auth__ReviewEmails__${i}', value: mail }]

var optionalSettings = concat(
  empty(playIntegrityPackageName) ? [] : [
    { name: 'Abuse__PlayIntegrity__DecryptionKey', secretRef: 'play-integrity-decryption-key' }
    { name: 'Abuse__PlayIntegrity__VerificationKey', secretRef: 'play-integrity-verification-key' }
  ],
  sentryEnabled ? [{ name: 'Telemetry__SentryDsn', secretRef: 'sentry-dsn' }] : [],
  reviewMode ? [{ name: 'Auth__ReviewCode', secretRef: 'review-code' }] : [],
  googleSettings,
  reviewEmailSettings
)

resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: appName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identityId}': {} }
  }
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      activeRevisionsMode: 'Single'
      secrets: secrets
      registries: empty(registryUsername) ? [] : [
        { server: registryServer, username: registryUsername, passwordSecretRef: 'registry-password' }
      ]
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        // Only Cloudflare may reach the origin. Empty list = open (first bring-up only).
        ipSecurityRestrictions: [for (cidr, i) in ipAllowList: {
          name: 'cloudflare-${i}'
          action: 'Allow'
          ipAddressRange: cidr
        }]
      }
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiImage
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: concat(settings, optionalSettings)
          probes: [
            { type: 'Startup', httpGet: { path: '/health', port: 8080 }, periodSeconds: 5, failureThreshold: 30 }
            { type: 'Liveness', httpGet: { path: '/health', port: 8080 }, periodSeconds: 30 }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
        rules: [
          { name: 'http', http: { metadata: { concurrentRequests: '50' } } }
        ]
      }
    }
  }
}

output fqdn string = api.properties.configuration.ingress.fqdn
output name string = api.name
