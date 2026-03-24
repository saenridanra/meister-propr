using './main.bicep'

param projectName     = 'meister-propr'
param location        = 'switzerlandnorth'
param aiEndpoint       = 'https://<your-ai-services-name>.cognitiveservices.azure.com'
param aiDeploymentName = 'gpt-5.1-codex-mini'
param imageTag        = 'latest'
param acrRepository   = 'myorg'         // ACR repository prefix
param spTenantId      = ''              // Azure AD tenant ID
param spClientId      = ''              // Service principal client ID
param spClientSecret  = ''              // Set via: az deployment group create --parameters spClientSecret=<value>

// Key Vault secrets — pass at deployment time, never commit values
param dbConnectionString = ''          // Host=...;Port=5432;Database=meisterpropr;...
param meisterAdminKey    = ''
param meisterClientKeys  = ''          // Comma-separated
param aiApiKey           = ''
param dbUser             = ''
param dbPassword         = ''
