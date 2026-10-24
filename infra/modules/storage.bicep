// One storage account, two kinds of containers:
//   public  (anonymous blob read, served through Cloudflare): works, avatars. Pixel-only variants, no EXIF.
//   private (no anonymous access, ever):                       originals, exports, evidence, dataprotection.
// Trade-off: the API reaches every container through one connection string, so one account is used and the account
// must allow container-level public access. Safety comes from each container's own publicAccess = 'None', which this
// template pins on every redeploy (a manual change is reverted) and the API also creates private by default.
// A second "public" account would let the private one set allowBlobPublicAccess=false, but needs a second client and
// connection string in the API for no cost saving; revisit if more public containers are ever added.
param name string
param location string
param keyVaultName string
@description('Origins allowed to read public images from a browser (the web viewer).')
param viewerOrigins array
@description('Principal that reads and writes blobs and signs SAS tokens with a user delegation key.')
param apiPrincipalId string
@description('Days after which data-export archives are deleted.')
param exportRetentionDays int = 7

var publicContainers = ['works', 'avatars']
var privateContainers = ['originals', 'exports', 'evidence', 'dataprotection']

resource account 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: name
  location: location
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    accessTier: 'Hot'
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: true // only works/avatars opt in below
    allowSharedKeyAccess: true // the API signs SAS URLs with the account key today
    allowCrossTenantReplication: false
    publicNetworkAccess: 'Enabled'
  }
}

resource blob 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: account
  name: 'default'
  properties: {
    // No soft delete: account deletion and take-downs must remove bytes now, not after a retention window.
    deleteRetentionPolicy: { enabled: false }
    containerDeleteRetentionPolicy: { enabled: false }
    cors: {
      corsRules: [
        {
          allowedOrigins: viewerOrigins
          allowedMethods: ['GET', 'HEAD']
          allowedHeaders: ['*']
          exposedHeaders: ['ETag', 'Content-Length', 'Content-Type']
          maxAgeInSeconds: 86400
        }
      ]
    }
  }
}

resource publicContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = [for c in publicContainers: {
  parent: blob
  name: c
  properties: { publicAccess: 'Blob' }
}]

resource privateContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = [for c in privateContainers: {
  parent: blob
  name: c
  properties: { publicAccess: 'None' }
}]

// Lifecycle: only data that is provably temporary. Originals are never aged out here: they are needed to rebuild
// variants and as take-down proof, and uploaded-but-unpublished drafts are removed by the API's DraftCleanupJob
// (their blob names are indistinguishable from published ones, so an age rule on originals would delete real work).
// Evidence is held at least a year and is never touched by lifecycle.
resource lifecycle 'Microsoft.Storage/storageAccounts/managementPolicies@2023-05-01' = {
  parent: account
  name: 'default'
  properties: {
    policy: {
      rules: [
        {
          name: 'delete-old-exports'
          enabled: true
          type: 'Lifecycle'
          definition: {
            filters: { blobTypes: ['blockBlob'], prefixMatch: ['exports/'] }
            actions: { baseBlob: { delete: { daysAfterModificationGreaterThan: exportRetentionDays } } }
          }
        }
      ]
    }
  }
}

var storageBlobDataContributor = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
var storageBlobDelegator = 'db58b8e5-c6ad-4a2a-8342-4190687cbf4a'

resource contributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: account
  name: guid(account.id, apiPrincipalId, storageBlobDataContributor)
  properties: {
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributor)
  }
}

resource delegator 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: account
  name: guid(account.id, apiPrincipalId, storageBlobDelegator)
  properties: {
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDelegator)
  }
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource connectionSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'storage-connection-string'
  properties: {
    value: 'DefaultEndpointsProtocol=https;AccountName=${account.name};AccountKey=${account.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
  }
}

output accountName string = account.name
output blobEndpoint string = account.properties.primaryEndpoints.blob
