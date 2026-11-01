// Free-tier Static Web App that hosts the Flutter web story viewer. Deploy the build with the SWA deployment token
// (`az staticwebapp secrets list`) or GitHub Actions; no content is deployed by this template.
param name string
@description('Free SKU supports only these regions: westus2, centralus, eastus2, westeurope, eastasia.')
param location string = 'eastus2'

resource site 'Microsoft.Web/staticSites@2023-12-01' = {
  name: name
  location: location
  sku: { name: 'Free', tier: 'Free' }
  properties: {
    stagingEnvironmentPolicy: 'Disabled'
    allowConfigFileUpdates: true
  }
}

output defaultHostname string = site.properties.defaultHostname
output name string = site.name
