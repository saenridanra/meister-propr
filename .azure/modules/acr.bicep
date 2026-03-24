param location string
param acrName string

resource acr 'Microsoft.ContainerRegistry/registries@2025-11-01' = {
  name: acrName
  location: location
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: false
    publicNetworkAccess: 'Enabled'
    anonymousPullEnabled: false
    policies: {
      azureADAuthenticationAsArmPolicy: { status: 'enabled' }
    }
  }
}

output acrLoginServer string = acr.properties.loginServer
