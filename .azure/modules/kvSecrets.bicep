param kvName string

@secure()
param dbConnectionString string
@secure()
param meisterAdminKey string
@secure()
param meisterClientKeys string
@secure()
param aiApiKey string
@secure()
param dbUser string
@secure()
param dbPassword string
@secure()
param azureClientSecret string

resource kv 'Microsoft.KeyVault/vaults@2025-05-01' existing = {
  name: kvName
}

resource secretDbConnectionString 'Microsoft.KeyVault/vaults/secrets@2025-05-01' = {
  parent: kv
  name: 'DB-CONNECTIONSTRING'
  properties: { value: dbConnectionString }
}

resource secretAdminKey 'Microsoft.KeyVault/vaults/secrets@2025-05-01' = {
  parent: kv
  name: 'MEISTER-ADMIN-KEY'
  properties: { value: meisterAdminKey }
}

resource secretClientKeys 'Microsoft.KeyVault/vaults/secrets@2025-05-01' = {
  parent: kv
  name: 'MEISTER-CLIENT-KEYS'
  properties: { value: meisterClientKeys }
}

resource secretAiApiKey 'Microsoft.KeyVault/vaults/secrets@2025-05-01' = {
  parent: kv
  name: 'AI-API-KEY'
  properties: { value: aiApiKey }
}

resource secretDbUser 'Microsoft.KeyVault/vaults/secrets@2025-05-01' = {
  parent: kv
  name: 'DB-USER'
  properties: { value: dbUser }
}

resource secretDbPassword 'Microsoft.KeyVault/vaults/secrets@2025-05-01' = {
  parent: kv
  name: 'DB-PASSWORD'
  properties: { value: dbPassword }
}

resource secretAzureClientSecret 'Microsoft.KeyVault/vaults/secrets@2025-05-01' = {
  parent: kv
  name: 'AZURE-CLIENT-SECRET'
  properties: { value: azureClientSecret }
}
