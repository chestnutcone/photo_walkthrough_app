// PostgreSQL Flexible Server: smallest burstable SKU, 7-day point-in-time restore, TLS 1.2+ only, pg_trgm allow-listed
// (the search migration runs CREATE EXTENSION pg_trgm, which Azure refuses unless it is in azure.extensions).
param name string
param location string
param keyVaultName string
param administratorLogin string
@secure()
@description('Server admin password. Pass it on the first deployment and on rotation; leave empty on later deployments to keep the current one. It is also written to Key Vault inside the connection string.')
param administratorPassword string = ''
param databaseName string = 'vela'
@description('Smallest burstable size.')
param skuName string = 'Standard_B1ms'
param storageSizeGB int = 32
@minValue(7)
@maxValue(35)
param backupRetentionDays int = 7

resource server 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: name
  location: location
  sku: { name: skuName, tier: 'Burstable' }
  properties: {
    version: '16'
    administratorLogin: administratorLogin
    administratorLoginPassword: empty(administratorPassword) ? null : administratorPassword
    authConfig: { activeDirectoryAuth: 'Disabled', passwordAuth: 'Enabled' }
    storage: { storageSizeGB: storageSizeGB, autoGrow: 'Disabled' }
    backup: { backupRetentionDays: backupRetentionDays, geoRedundantBackup: 'Disabled' }
    highAvailability: { mode: 'Disabled' }
    network: { publicNetworkAccess: 'Enabled' }
  }
}

resource extensions 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: server
  name: 'azure.extensions'
  properties: { value: 'PG_TRGM', source: 'user-override' }
}

resource requireTls 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: server
  name: 'require_secure_transport'
  properties: { value: 'ON', source: 'user-override' }
  dependsOn: [extensions] // configuration writes on one server must not run in parallel
}

resource minTls 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: server
  name: 'ssl_min_protocol_version'
  properties: { value: 'TLSv1.2', source: 'user-override' }
  dependsOn: [requireTls]
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: server
  name: databaseName
  properties: { charset: 'UTF8', collation: 'en_US.utf8' }
}

// Container Apps on the consumption plan have no fixed outbound address, so the cheapest allow rule is "Azure services".
// The server is still reachable only with the admin password over TLS; move to a delegated subnet if that is not enough.
resource allowAzure 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = {
  parent: server
  name: 'AllowAllAzureServicesAndResourcesWithinAzureIps'
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource connectionSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!empty(administratorPassword)) {
  parent: vault
  name: 'postgres-connection-string'
  properties: {
    value: 'Host=${server.properties.fullyQualifiedDomainName};Port=5432;Database=${databaseName};Username=${administratorLogin};Password=${administratorPassword};Ssl Mode=Require;Trust Server Certificate=false'
  }
}

output serverName string = server.name
output fqdn string = server.properties.fullyQualifiedDomainName
