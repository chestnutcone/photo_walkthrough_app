// Holds every secret the API reads. Secrets that infrastructure generates (storage, ACS, Application Insights,
// Postgres connection strings) are written by the modules that own them; the owner adds the rest once, with
// `az keyvault secret set` (see docs/deploy.md). Nothing secret is ever stored in a file or a parameter file.
param name string
param location string
param tenantId string = subscription().tenantId
@description('Principal that may read secrets (the API identity).')
param readerPrincipalId string
@description('Dev vaults can be purged; production should keep purge protection on.')
param purgeProtection bool = false

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: name
  location: location
  properties: {
    tenantId: tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    // The property may only be set to true: sending false is rejected, so it is omitted for dev.
    enablePurgeProtection: purgeProtection ? true : null
    publicNetworkAccess: 'Enabled'
  }
}

var keyVaultSecretsUser = '4633458b-17de-408a-b874-0445c86b69e6'

resource readSecrets 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: vault
  name: guid(vault.id, readerPrincipalId, keyVaultSecretsUser)
  properties: {
    principalId: readerPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUser)
  }
}

output name string = vault.name
output uri string = vault.properties.vaultUri
