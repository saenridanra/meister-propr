@description('Base name for all resources. Drives every resource name in the deployment.')
param projectName string = 'meister-propr'

@description('Azure region for all resources.')
param location string = 'switzerlandnorth'

@description('Endpoint URL of the existing Azure AI Services resource.')
param aiEndpoint string

@description('AI model deployment name.')
param aiDeploymentName string = 'gpt-5.1-codex-mini'

@description('Container image tag for backend, admin-ui, and reverse-proxy.')
param imageTag string = 'latest'

@description('Set to false on first deploy to provision infrastructure before images exist in ACR.')
param deployApps bool = true

@description('ACR repository prefix (e.g. "myorg").')
param acrRepository string

@description('Azure AD tenant ID for the backend service principal.')
param spTenantId string

@description('Azure AD client ID for the backend service principal.')
param spClientId string

@description('Azure AD client secret for the backend service principal.')
@secure()
param spClientSecret string

// ── Key Vault secret values ───────────────────────────────────────────────────
@description('PostgreSQL connection string stored in Key Vault.')
@secure()
param dbConnectionString string

@description('Admin API key (X-Admin-Key header) stored in Key Vault.')
@secure()
param meisterAdminKey string

@description('Comma-separated client keys stored in Key Vault.')
@secure()
param meisterClientKeys string

@description('Azure OpenAI API key stored in Key Vault.')
@secure()
param aiApiKey string

@description('PostgreSQL username stored in Key Vault.')
@secure()
param dbUser string

@description('PostgreSQL password stored in Key Vault.')
@secure()
param dbPassword string

// ── Derived names ─────────────────────────────────────────────────────────────
// ACR and storage account names must be alphanumeric only
var safeName    = replace(projectName, '-', '')
var acrName     = '${safeName}acr'
var storageName = '${safeName}sg'
var kvName      = 'kv${safeName}${take(uniqueString(resourceGroup().id), 4)}'

// ── Modules ───────────────────────────────────────────────────────────────────

module observability 'modules/observability.bicep' = {
  name: 'observability'
  params: {
    location: location
    workspaceName: 'workspace-${projectName}'
  }
}

module network 'modules/network.bicep' = {
  name: 'network'
  params: {
    location: location
    vnetName: '${projectName}-vnet'
  }
}

module acr 'modules/acr.bicep' = {
  name: 'acr'
  params: {
    location: location
    acrName: acrName
  }
}

module keyvault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  params: {
    location: location
    kvName: kvName
    subnetId: network.outputs.subnetId
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    location: location
    storageName: storageName
  }
}

module kvSecrets 'modules/kvSecrets.bicep' = {
  name: 'kvSecrets'
  params: {
    kvName: kvName
    dbConnectionString: dbConnectionString
    meisterAdminKey: meisterAdminKey
    meisterClientKeys: meisterClientKeys
    aiApiKey: aiApiKey
    dbUser: dbUser
    dbPassword: dbPassword
    azureClientSecret: spClientSecret
  }
  dependsOn: [keyvault]
}

module containerEnvironment 'modules/containerEnvironment.bicep' = {
  name: 'containerEnvironment'
  params: {
    location: location
    envName: 'managedEnvironment-${projectName}'
    workspaceId: observability.outputs.workspaceId
    workspaceKey: observability.outputs.workspaceKey
    storageAccountName: storageName
    storageAccountKey: storage.outputs.storageAccountKey
    infrastructureSubnetId: network.outputs.subnetId
  }
}

module containerApps 'modules/containerApps.bicep' = if (deployApps) {
  name: 'containerApps'
  params: {
    location: location
    projectName: projectName
    envName: 'managedEnvironment-${projectName}'
    envDefaultDomain: containerEnvironment.outputs.defaultDomain
    acrName: acrName
    acrRepository: acrRepository
    imageTag: imageTag
    kvName: kvName
    storageAccountName: storageName
    aiEndpoint: aiEndpoint
    aiDeploymentName: aiDeploymentName
    spTenantId: spTenantId
    spClientId: spClientId
  }
  dependsOn: [kvSecrets]
}

// ── Outputs ───────────────────────────────────────────────────────────────────
output reverseProxyUrl string = deployApps ? 'https://${containerApps.outputs.reverseProxyFqdn}' : 'not deployed'
output acrLoginServer string = acr.outputs.acrLoginServer
output kvUri string = keyvault.outputs.kvUri
