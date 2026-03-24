# Azure Infrastructure (Bicep)

Deploys the full meister-propr stack to Azure Container Apps.

Note: This is only one way of many to host meister-propr in Azure.

## Structure

```
.azure/
  main.bicep                        # Entry point — all parameters, derived names, module wiring
  main.bicepparam                   # Parameter file (fill in before deploying, never commit secrets)
  deploy.ps1                        # End-to-end deployment script (build → infra → push → apps)
  modules/
    observability.bicep             # Log Analytics workspace
    network.bicep                   # VNet + infrastructure subnet (delegated to Container Apps)
    acr.bicep                       # Azure Container Registry
    keyvault.bicep                  # Key Vault (RBAC mode)
    kvSecrets.bicep                 # Key Vault secret values
    storage.bicep                   # Storage account + postgres-data file share (Azure Files)
    containerEnvironment.bicep      # Container Apps managed environment + VNet + storage mount
    containerApps.bicep             # All four container apps + ACR pull / KV access role assignments
```

## Deployed resources

| Resource | Name pattern | Notes |
|---|---|---|
| Log Analytics workspace | `workspace-{projectName}` | |
| Virtual network | `{projectName}-vnet` | Subnet `10.0.0.0/23` delegated to `Microsoft.App/environments` |
| Container Registry | `{projectName-no-hyphens}acr` | Admin user disabled; uses managed identity pull |
| Key Vault | `kv{projectName-no-hyphens}{suffix}` | RBAC mode; suffix is deterministic from resource group ID |
| Storage account | `{projectName-no-hyphens}sg` | Azure Files share `postgres-data` for PostgreSQL volume |
| Container Apps environment | `managedEnvironment-{projectName}` | System-assigned identity; VNet-integrated |
| Container App: reverse proxy | `{projectName}-reverse-proxy` | External ingress; public entry point |
| Container App: backend | `{projectName}-backend` | Internal ingress; secrets from Key Vault |
| Container App: admin-ui | `{projectName}-admin-ui` | Internal ingress |
| Container App: db | `{projectName}-db` | Internal ingress; TCP on 5432; Azure Files volume |

## Parameters

| Parameter | Required | Default | Description |
|---|---|---|---|
| `projectName` | | `meister-propr` | Drives all resource names |
| `location` | | `switzerlandnorth` | Azure region |
| `aiEndpoint` | Yes | | Endpoint of the existing Azure AI Services resource |
| `aiDeploymentName` | | `gpt-5.1-codex-mini` | Model deployment name |
| `imageTag` | | `latest` | Container image tag for all three app images |
| `acrRepository` | Yes | | Repository prefix in ACR (e.g. `myorg`) |
| `deployApps` | | `true` | Set to `false` on first deploy before images exist |
| `spTenantId` | Yes | | Service principal Azure AD tenant ID |
| `spClientId` | Yes | | Service principal client ID |
| `spClientSecret` | Yes | | Service principal client secret (**secure**) |
| `dbConnectionString` | Yes | | PostgreSQL connection string (**secure**, stored in Key Vault) |
| `meisterAdminKey` | Yes | | `X-Admin-Key` value (**secure**, stored in Key Vault) |
| `meisterClientKeys` | Yes | | Comma-separated client keys (**secure**, stored in Key Vault) |
| `aiApiKey` | Yes | | Azure OpenAI API key (**secure**, stored in Key Vault) |
| `dbUser` | Yes | | PostgreSQL username (**secure**, stored in Key Vault) |
| `dbPassword` | Yes | | PostgreSQL password (**secure**, stored in Key Vault) |

Fill in non-secret values in `main.bicepparam`. Pass all `@secure()` parameters on the command line — never commit them.

## Deployment

### Prerequisites

- PowerShell 7+
- Azure CLI (`az bicep install` for Bicep support)
- Docker **or** Podman
- An existing resource group
- An existing Azure AI Services resource (OpenAI)

### Using the deploy script (recommended)

`deploy.ps1` handles the full lifecycle in order: build images → deploy infrastructure → push images → deploy container apps. It must be run from the **repository root**.

#### Interactive — prompts for anything not supplied

```powershell
./.azure/deploy.ps1 -ResourceGroup my-rg
```

#### Fully scripted via config object

```powershell
$cfg = @{
    ResourceGroup      = 'my-rg'
    AcrRepository      = 'myorg'
    AiEndpoint         = 'https://myai.cognitiveservices.azure.com'
    SpTenantId         = '...'
    SpClientId         = '...'
    SpClientSecret     = '...'        # plain string or SecureString
    DbConnectionString = 'Host=...;Port=5432;Database=meisterpropr;Username=...;Password=...;Ssl Mode=Require'
    MeisterAdminKey    = '...'
    MeisterClientKeys  = '...'
    AiApiKey           = '...'
    DbUser             = 'postgres'
    DbPassword         = '...'
}
./.azure/deploy.ps1 -Config $cfg
```

Individual parameters always take precedence over `Config` values, so you can mix both:

```powershell
./.azure/deploy.ps1 -Config $cfg -ImageTag v2.1
```

#### Container tool detection

The script prefers **Docker** and falls back to **Podman** automatically. The selected tool is shown in the deployment summary before you confirm.

> **Podman note:** `az acr login` only configures the Docker credential store. When Podman is detected the script uses `az acr login --expose-token` to obtain a short-lived registry token and logs in directly via `podman login`. No ACR admin user required.

### Manual deployment (Bicep only)

If you prefer to run the Bicep steps yourself:

**Step 1 — provision infrastructure** (before images exist in ACR):

```bash
az deployment group create \
  --resource-group <your-rg> \
  --template-file .azure/main.bicep \
  --parameters .azure/main.bicepparam \
  --parameters deployApps=false \
               spClientSecret="..." \
               dbConnectionString="Host=...;Port=5432;Database=meisterpropr;Username=...;Password=...;Ssl Mode=Require" \
               meisterAdminKey="..." \
               meisterClientKeys="..." \
               aiApiKey="..." \
               dbUser="..." \
               dbPassword="..."
```

**Step 2 — build and push images:**

```bash
ACR=$(az deployment group show \
  --resource-group <your-rg> --name main \
  --query properties.outputs.acrLoginServer.value -o tsv)

az acr login --name ${ACR%%.*}

docker build -t $ACR/<repo>/meister-propr:<tag>               -f Dockerfile .
docker build -t $ACR/<repo>/meister-propr-admin-ui:<tag>      -f admin-ui/Dockerfile .
docker build -t $ACR/<repo>/meister-propr-reverse-proxy:<tag> -f nginx/Dockerfile ./nginx

docker push $ACR/<repo>/meister-propr:<tag>
docker push $ACR/<repo>/meister-propr-admin-ui:<tag>
docker push $ACR/<repo>/meister-propr-reverse-proxy:<tag>
```

**Step 3 — deploy container apps:**

```bash
az deployment group create \
  --resource-group <your-rg> \
  --template-file .azure/main.bicep \
  --parameters .azure/main.bicepparam \
  --parameters deployApps=true imageTag=<tag> \
               spClientSecret="..." \
               # ... all secrets
```

The reverse proxy URL is a deployment output:

```bash
az deployment group show \
  --resource-group <your-rg> --name main \
  --query properties.outputs.reverseProxyUrl.value -o tsv
```

### Subsequent deployments

Re-run the deploy script (or Step 3 manually) with a new `ImageTag` to roll out updated images. Infrastructure only needs redeployment when Bicep modules change.

## Image pull authentication

The Container Apps managed environment has a system-assigned managed identity. Each container app uses `identity: system-environment` for its ACR registry entry. The `containerApps` module grants the environment's identity the `AcrPull` role on the ACR — no credentials or admin passwords required.

## Secret management

All runtime secrets are stored in Key Vault and referenced by the backend container app via Key Vault secret references (`keyVaultUrl` + `identity: system`). The backend's system-assigned identity is granted `Key Vault Secrets User` on the vault. The same applies to the db container app for `DB-USER` and `DB-PASSWORD`.
