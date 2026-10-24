// Vela infrastructure. Subscription scope: creates the resource group and everything in it.
//   what-if : az deployment sub what-if  --location eastus2 --template-file infra/main.bicep --parameters infra/parameters.dev.json
//   deploy  : via the infra workflow (workflow_dispatch) or docs/deploy.md
// Bring-up is two passes: deployApp=false creates everything the app needs, the owner then stores the secrets in Key
// Vault, and deployApp=true adds the Container App. See docs/deploy.md.
targetScope = 'subscription'

@allowed(['dev', 'staging', 'prod'])
param environment string = 'dev'
@description('Azure region. The market is the United States.')
param location string = 'eastus2'
param postgresAdminLogin string
@secure()
@description('Pass on first deployment or rotation only (from your shell or the infra workflow); empty keeps the current password.')
param postgresAdminPassword string = ''
@description('Planned monthly spend in USD. The budget alert fires at 2x this.')
param monthlyBudget int
param budgetAlertEmails array
@description('Fixed budget start, the first day of a month (yyyy-MM-01). Never change it after the first deployment.')
param budgetStartDate string

@description('Domain of the share pages and API, proxied by Cloudflare, e.g. vela.example. Must differ from the viewer host.')
param brandingDomain string
@description('Host of the images CDN (Cloudflare CNAME to the storage account), e.g. img.vela.example. Empty uses the raw blob endpoint.')
param imageHost string = ''
@description('Custom host for the web viewer. Empty uses the Static Web App default hostname.')
param viewerHost string = ''
@description('Email sending domain, e.g. mail.vela.example.')
param emailSendingDomain string
param emailSenderLocalPart string = 'donotreply'
@description('Set true after the sending domain shows Verified in the portal.')
param linkEmailDomain bool = false

@description('Container App outbound IPs for the Postgres firewall. Fill in after pass 2, then set allowAzureServices=false.')
param appOutboundIps array = []
param allowAzureServices bool = true
param deployApp bool = false
@description('Full image reference, e.g. ghcr.io/owner/vela-api:1.0.0. Required when deployApp is true.')
param apiImage string = ''
param registryServer string = 'ghcr.io'
param registryUsername string = ''
@description('Replicas kept running. -1 = 1 in prod, 0 elsewhere. The API\'s background workers (take-down reconcile, account deletion, exports, evidence retention, draft cleanup) only run while a replica is up, so prod must not scale to zero.')
param minReplicas int = -1
param maxReplicas int = 2
@description('Cloudflare IPv4 ranges allowed to reach the API origin (https://www.cloudflare.com/ips-v4). Empty disables the restriction.')
param cloudflareIpv4Ranges array = []

param contentSafetyEndpoint string = ''
param cloudflareZoneId string = ''
param adminEmails string = ''
param appleClientId string = ''
param googleClientIds array = []
param appleTeamId string = ''
param androidSha256Fingerprints array = []
param dmcaAgentName string = ''
param dmcaAgentEmail string = ''
param dmcaAgentAddress string = ''
@description('MUST be true at launch. False only for first bring-up before the store apps and verifier settings exist.')
param requireAttestation bool = true
param playIntegrityPackageName string = ''
param appAttestAppId string = ''
param reviewMode bool = false
param reviewEmails array = []
param sentryEnabled bool = true

var effectiveMinReplicas = minReplicas >= 0 ? minReplicas : (environment == 'prod' ? 1 : 0)
var prefix = 'vela-${environment}'
var suffix = take(uniqueString(subscription().subscriptionId, prefix), 6)
var staticWebLocations = ['westus2', 'centralus', 'eastus2', 'westeurope', 'eastasia']
var staticWebLocation = contains(staticWebLocations, location) ? location : 'eastus2'

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-${prefix}'
  location: location
}

module identity 'modules/identity.bicep' = {
  scope: rg
  name: 'identity'
  params: { name: 'id-${prefix}-api', location: location }
}

module vault 'modules/keyvault.bicep' = {
  scope: rg
  name: 'keyvault'
  params: {
    name: 'kv-${prefix}-${suffix}'
    location: location
    readerPrincipalId: identity.outputs.principalId
    purgeProtection: environment == 'prod'
  }
}

module logs 'modules/loganalytics.bicep' = {
  scope: rg
  name: 'loganalytics'
  params: { name: 'log-${prefix}', location: location, keyVaultName: vault.outputs.name }
}

module web 'modules/staticweb.bicep' = {
  scope: rg
  name: 'staticweb'
  params: { name: 'swa-${prefix}', location: staticWebLocation }
}

var viewerOrigin = empty(viewerHost) ? 'https://${web.outputs.defaultHostname}' : 'https://${viewerHost}'

module storage 'modules/storage.bicep' = {
  scope: rg
  name: 'storage'
  params: {
    name: 'vela${environment}${suffix}'
    location: location
    keyVaultName: vault.outputs.name
    // Both the default hostname and the custom host may serve the viewer.
    viewerOrigins: union(['https://${web.outputs.defaultHostname}'], empty(viewerHost) ? [] : ['https://${viewerHost}'])
    apiPrincipalId: identity.outputs.principalId
  }
}

module postgres 'modules/postgres.bicep' = {
  scope: rg
  name: 'postgres'
  params: {
    name: 'psql-${prefix}-${suffix}'
    location: location
    keyVaultName: vault.outputs.name
    administratorLogin: postgresAdminLogin
    administratorPassword: postgresAdminPassword
    appOutboundIps: appOutboundIps
    allowAzureServices: allowAzureServices
  }
}

module email 'modules/acs-email.bicep' = {
  scope: rg
  name: 'acs-email'
  params: {
    name: 'acs-${prefix}-${suffix}'
    keyVaultName: vault.outputs.name
    sendingDomain: emailSendingDomain
    linkDomain: linkEmailDomain
  }
}

module budget 'modules/budget.bicep' = {
  scope: rg
  name: 'budget'
  params: { name: 'budget-${prefix}', monthlyBudget: monthlyBudget, startDate: budgetStartDate, contactEmails: budgetAlertEmails }
}

var publicBaseUrl = empty(imageHost) ? storage.outputs.blobEndpoint : 'https://${imageHost}/'

module app 'modules/containerapp.bicep' = if (deployApp) {
  scope: rg
  name: 'containerapp'
  params: {
    environmentName: 'cae-${prefix}'
    appName: 'ca-${prefix}-api'
    location: location
    logAnalyticsName: logs.outputs.workspaceName
    identityId: identity.outputs.id
    keyVaultUri: vault.outputs.uri
    apiImage: apiImage
    registryServer: registryServer
    registryUsername: registryUsername
    minReplicas: effectiveMinReplicas
    maxReplicas: maxReplicas
    ipAllowList: cloudflareIpv4Ranges
    brandingDomain: brandingDomain
    viewerUrl: viewerOrigin
    publicBaseUrl: publicBaseUrl
    adminEmails: adminEmails
    emailSenderAddress: '${emailSenderLocalPart}@${emailSendingDomain}'
    contentSafetyEndpoint: contentSafetyEndpoint
    cloudflareZoneId: cloudflareZoneId
    links: { appleTeamId: appleTeamId, sha256Fingerprints: androidSha256Fingerprints }
    legal: { agentName: dmcaAgentName, agentEmail: dmcaAgentEmail, agentAddress: dmcaAgentAddress }
    googleClientIds: googleClientIds
    appleClientId: appleClientId
    sentryEnabled: sentryEnabled
    requireAttestation: requireAttestation
    playIntegrityPackageName: playIntegrityPackageName
    appAttestAppId: appAttestAppId
    reviewMode: reviewMode
    reviewEmails: reviewEmails
  }
  dependsOn: [postgres, email]
}

output resourceGroupName string = rg.name
output apiUrl string = deployApp ? 'https://${app.?outputs.fqdn}' : ''
output storageAccountName string = storage.outputs.accountName
output webViewerUrl string = viewerOrigin
output keyVaultName string = vault.outputs.name
output staticWebAppName string = web.outputs.name
output postgresServerName string = postgres.outputs.serverName
output emailDomainVerification object = email.outputs.domainVerification
