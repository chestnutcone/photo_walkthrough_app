// User-assigned identity for the API. Created before the app so Key Vault and Storage roles exist
// when the Container App first tries to resolve its secret references.
param name string
param location string

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: name
  location: location
}

output id string = identity.id
output principalId string = identity.properties.principalId
