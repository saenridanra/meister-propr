param location string
param storageName string

resource storage 'Microsoft.Storage/storageAccounts@2025-06-01' = {
  name: storageName
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
    accessTier: 'Hot'
  }
}

resource fileServices 'Microsoft.Storage/storageAccounts/fileServices@2025-06-01' = {
  parent: storage
  name: 'default'
}

resource postgresShare 'Microsoft.Storage/storageAccounts/fileServices/shares@2025-06-01' = {
  parent: fileServices
  name: 'postgres-data'
  properties: {
    shareQuota: 10
    enabledProtocols: 'SMB'
  }
}

#disable-next-line outputs-should-not-contain-secrets
output storageAccountKey string = listKeys(storage.id, storage.apiVersion).keys[0].value
output storageAccountName string = storage.name
