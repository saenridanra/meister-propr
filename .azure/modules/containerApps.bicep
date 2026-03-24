param location string
param projectName string
param envName string
param envDefaultDomain string
param acrName string
param acrRepository string
param imageTag string
param kvName string
param storageAccountName string
param aiEndpoint string
param aiDeploymentName string
param spTenantId string
param spClientId string

var acrServer  = '${acrName}.azurecr.io'
var kvUri      = 'https://${kvName}.vault.azure.net'
var adminUiFqdn = '${projectName}-admin-ui.internal.${envDefaultDomain}'
var backendFqdn = '${projectName}-backend.internal.${envDefaultDomain}'

// Built-in role definition IDs
var acrPullRoleId          = '7f951dda-4ed3-4680-a7ca-43fe172d538d'
var kvSecretsUserRoleId    = '4633458b-17de-408a-b874-0445c86b69e6'

resource env 'Microsoft.App/managedEnvironments@2025-10-02-preview' existing = {
  name: envName
}

resource acr 'Microsoft.ContainerRegistry/registries@2025-11-01' existing = {
  name: acrName
}

resource kv 'Microsoft.KeyVault/vaults@2025-05-01' existing = {
  name: kvName
}

// ── Role: managed environment → ACR (pull images) ────────────────────────────
resource envAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(env.id, acr.id, acrPullRoleId)
  scope: acr
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: env.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// ── PostgreSQL ────────────────────────────────────────────────────────────────
resource db 'Microsoft.App/containerapps@2025-10-02-preview' = {
  name: '${projectName}-db'
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {
    managedEnvironmentId: env.id
    environmentId: env.id
    workloadProfileName: 'Consumption'
    configuration: {
      secrets: [
        { name: 'db-password', keyVaultUrl: '${kvUri}/secrets/DB-PASSWORD', identity: 'system' }
        { name: 'db-user',     keyVaultUrl: '${kvUri}/secrets/DB-USER',     identity: 'system' }
      ]
      activeRevisionsMode: 'Single'
      ingress: {
        external: false
        targetPort: 5432
        exposedPort: 5432
        transport: 'Tcp'
        traffic: [{ weight: 100, latestRevision: true }]
        allowInsecure: false
      }
      registries: [{ server: acrServer, identity: 'system-environment' }]
    }
    template: {
      containers: [
        {
          image: '${acrServer}/postgres:17-alpine'
          name: '${projectName}-db'
          env: [
            { name: 'POSTGRES_DB',       value: 'meisterpropr' }
            { name: 'POSTGRES_USER',     secretRef: 'db-user' }
            { name: 'POSTGRES_PASSWORD', secretRef: 'db-password' }
          ]
          resources: { cpu: json('0.5'), memory: '1Gi' }
          probes: [
            { type: 'Liveness',  failureThreshold: 3,   periodSeconds: 10, successThreshold: 1, tcpSocket: { port: 5432 }, timeoutSeconds: 5 }
            { type: 'Readiness', failureThreshold: 48,  periodSeconds: 5,  successThreshold: 1, tcpSocket: { port: 5432 }, timeoutSeconds: 5 }
            { type: 'Startup',   failureThreshold: 240, periodSeconds: 1,  successThreshold: 1, initialDelaySeconds: 1, tcpSocket: { port: 5432 }, timeoutSeconds: 3 }
          ]
          volumeMounts: [{ volumeName: 'db-storage', mountPath: '/var/lib/postgresql/data' }]
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 1 }
      volumes: [{ name: 'db-storage', storageType: 'AzureFile', storageName: storageAccountName }]
    }
  }
}

resource dbKvAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(db.id, kv.id, kvSecretsUserRoleId)
  scope: kv
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvSecretsUserRoleId)
    principalId: db.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// ── Backend ───────────────────────────────────────────────────────────────────
resource backend 'Microsoft.App/containerapps@2025-10-02-preview' = {
  name: '${projectName}-backend'
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {
    managedEnvironmentId: env.id
    environmentId: env.id
    workloadProfileName: 'Consumption'
    configuration: {
      secrets: [
        { name: 'db-connectionstring', keyVaultUrl: '${kvUri}/secrets/DB-CONNECTIONSTRING', identity: 'system' }
        { name: 'meister-admin-key',   keyVaultUrl: '${kvUri}/secrets/MEISTER-ADMIN-KEY',   identity: 'system' }
        { name: 'meister-client-keys', keyVaultUrl: '${kvUri}/secrets/MEISTER-CLIENT-KEYS', identity: 'system' }
        { name: 'ai-api-key',          keyVaultUrl: '${kvUri}/secrets/AI-API-KEY',           identity: 'system' }
        { name: 'azure-client-secret', keyVaultUrl: '${kvUri}/secrets/AZURE-CLIENT-SECRET', identity: 'system' }
      ]
      activeRevisionsMode: 'Single'
      ingress: {
        external: false
        targetPort: 8080
        transport: 'Auto'
        traffic: [{ weight: 100, latestRevision: true }]
        allowInsecure: true
      }
      registries: [{ server: acrServer, identity: 'system-environment' }]
    }
    template: {
      containers: [
        {
          image: '${acrServer}/${acrRepository}/${projectName}:${imageTag}'
          name: '${projectName}-backend'
          env: [
            { name: 'MEISTER_CLIENT_KEYS',       secretRef: 'meister-client-keys' }
            { name: 'MEISTER_ADMIN_KEY',          secretRef: 'meister-admin-key' }
            { name: 'ASPNETCORE_ENVIRONMENT',     value: 'Production' }
            { name: 'AI_ENDPOINT',                value: aiEndpoint }
            { name: 'AI_DEPLOYMENT',              value: aiDeploymentName }
            { name: 'AI_API_KEY',                 secretRef: 'ai-api-key' }
            { name: 'ADO_SKIP_TOKEN_VALIDATION',  value: 'false' }
            { name: 'DB_CONNECTION_STRING',       secretRef: 'db-connectionstring' }
            { name: 'AZURE_TENANT_ID',            value: spTenantId }
            { name: 'AZURE_CLIENT_ID',            value: spClientId }
            { name: 'AZURE_CLIENT_SECRET',        secretRef: 'azure-client-secret' }
          ]
          resources: { cpu: json('0.5'), memory: '1Gi' }
          probes: [
            { type: 'Liveness',  failureThreshold: 3,   periodSeconds: 10, successThreshold: 1, tcpSocket: { port: 8080 }, timeoutSeconds: 5 }
            { type: 'Readiness', failureThreshold: 48,  periodSeconds: 5,  successThreshold: 1, tcpSocket: { port: 8080 }, timeoutSeconds: 5 }
            { type: 'Startup',   failureThreshold: 240, periodSeconds: 1,  successThreshold: 1, initialDelaySeconds: 1, tcpSocket: { port: 8080 }, timeoutSeconds: 3 }
          ]
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
}

resource backendKvAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(backend.id, kv.id, kvSecretsUserRoleId)
  scope: kv
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvSecretsUserRoleId)
    principalId: backend.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// ── Admin UI ──────────────────────────────────────────────────────────────────
resource adminUi 'Microsoft.App/containerapps@2025-10-02-preview' = {
  name: '${projectName}-admin-ui'
  location: location
  identity: { type: 'None' }
  properties: {
    managedEnvironmentId: env.id
    environmentId: env.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: false
        targetPort: 80
        transport: 'Auto'
        traffic: [{ weight: 100, latestRevision: true }]
        allowInsecure: true
      }
      registries: [{ server: acrServer, identity: 'system-environment' }]
    }
    template: {
      containers: [
        {
          image: '${acrServer}/${acrRepository}/${projectName}-admin-ui:${imageTag}'
          name: '${projectName}-admin-ui'
          resources: { cpu: json('0.5'), memory: '1Gi' }
        }
      ]
      scale: { minReplicas: 0, maxReplicas: 10 }
    }
  }
}

// ── Reverse proxy ─────────────────────────────────────────────────────────────
resource reverseProxy 'Microsoft.App/containerapps@2025-10-02-preview' = {
  name: '${projectName}-reverse-proxy'
  location: location
  identity: { type: 'None' }
  properties: {
    managedEnvironmentId: env.id
    environmentId: env.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 80
        transport: 'Auto'
        traffic: [{ weight: 100, latestRevision: true }]
        allowInsecure: false
      }
      registries: [{ server: acrServer, identity: 'system-environment' }]
    }
    template: {
      containers: [
        {
          image: '${acrServer}/${acrRepository}/${projectName}-reverse-proxy:${imageTag}'
          name: '${projectName}-reverse-proxy'
          env: [
            { name: 'ADMIN_UI_HOST', value: adminUiFqdn }
            { name: 'BACKEND_HOST',  value: backendFqdn }
          ]
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          probes: [
            { type: 'Liveness',  failureThreshold: 3,   periodSeconds: 10, successThreshold: 1, tcpSocket: { port: 80 }, timeoutSeconds: 5 }
            { type: 'Readiness', failureThreshold: 48,  periodSeconds: 5,  successThreshold: 1, tcpSocket: { port: 80 }, timeoutSeconds: 5 }
            { type: 'Startup',   failureThreshold: 240, periodSeconds: 1,  successThreshold: 1, initialDelaySeconds: 1, tcpSocket: { port: 80 }, timeoutSeconds: 3 }
          ]
        }
      ]
      scale: { minReplicas: 0, maxReplicas: 10 }
    }
  }
}

output reverseProxyFqdn string = reverseProxy.properties.configuration.ingress.fqdn
