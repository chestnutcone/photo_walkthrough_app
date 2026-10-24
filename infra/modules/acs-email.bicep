// Azure Communication Services Email for sign-in codes and notices. Sending domain is the owner's (a parameter).
param name string
param keyVaultName string
@description('Data residency; US market.')
param dataLocation string = 'United States'
@description('Sending domain, e.g. mail.example.com. DNS records (SPF, DKIM, DKIM2, domain TXT) must be added by the owner.')
param sendingDomain string
@description('Link the domain to the Communication Service. Set true only after the domain shows Verified in the portal; linking an unverified domain fails.')
param linkDomain bool = false

resource emailService 'Microsoft.Communication/emailServices@2023-04-01' = {
  name: '${name}-email'
  location: 'global'
  properties: { dataLocation: dataLocation }
}

resource domain 'Microsoft.Communication/emailServices/domains@2023-04-01' = {
  parent: emailService
  name: sendingDomain
  location: 'global'
  properties: {
    domainManagement: 'CustomerManaged'
    userEngagementTracking: 'Disabled'
  }
}

resource communication 'Microsoft.Communication/communicationServices@2023-04-01' = {
  name: name
  location: 'global'
  properties: {
    dataLocation: dataLocation
    linkedDomains: linkDomain ? [domain.id] : []
  }
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource connectionSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'acs-connection-string'
  properties: { value: communication.listKeys().primaryConnectionString }
}

output senderDomain string = sendingDomain
output domainVerification object = domain.properties.verificationRecords
