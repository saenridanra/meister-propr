param location string
param envName string
param workspaceId string
@secure()
param workspaceKey string
param storageAccountName string
@secure()
param storageAccountKey string
param infrastructureSubnetId string

resource env 'Microsoft.App/managedEnvironments@2025-10-02-preview' = {
  name: envName
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: workspaceId
        sharedKey: workspaceKey
      }
    }
    vnetConfiguration: {
      infrastructureSubnetId: infrastructureSubnetId
      internal: false
    }
    zoneRedundant: false
    workloadProfiles: [
      {
        workloadProfileType: 'Consumption'
        name: 'Consumption'
        enableFips: false
      }
    ]
    peerAuthentication: { mtls: { enabled: false } }
    publicNetworkAccess: 'Enabled'
  }
}

// Azure Files mount used by the PostgreSQL container app
resource envStorage 'Microsoft.App/managedEnvironments/storages@2025-10-02-preview' = {
  parent: env
  name: storageAccountName
  properties: {
    azureFile: {
      accountName: storageAccountName
      accountKey: storageAccountKey
      shareName: 'postgres-data'
      accessMode: 'ReadWrite'
    }
  }
}

output envId string = env.id
output envPrincipalId string = env.identity.principalId
output defaultDomain string = env.properties.defaultDomain
