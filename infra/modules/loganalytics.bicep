// Log Analytics with a hard daily cap (about 4.5 GB a month) so logging can never become the biggest bill line.
param name string
param location string
param keyVaultName string
@description('GB per day. 0.15 keeps ingestion near the 5 GB free allowance.')
param dailyQuotaGb string = '0.15'

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: name
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    workspaceCapping: { dailyQuotaGb: json(dailyQuotaGb) }
    features: { disableLocalAuth: false }
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${name}-ai'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    IngestionMode: 'LogAnalytics'
    DisableIpMasking: false
  }
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource insightsSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'appinsights-connection-string'
  properties: { value: insights.properties.ConnectionString }
}

output workspaceName string = workspace.name
output workspaceId string = workspace.id
