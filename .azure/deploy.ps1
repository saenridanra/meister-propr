#Requires -Version 7.0
<#
.SYNOPSIS
    Deploys the full meister-propr stack to Azure Container Apps.

.DESCRIPTION
    1. Collects and validates all parameters
    2. Builds container images targeting ACR
    3. Deploys infrastructure via Bicep (deployApps=false)
    4. Pushes images to ACR
    5. Deploys container apps via Bicep (deployApps=true)

    Supports Docker or Podman — whichever is installed.

.PARAMETER Config
    Hashtable supplying any or all parameters. Individual parameters take
    precedence over Config values. Secrets may be plain strings or SecureStrings.

.EXAMPLE
    # Interactive — prompts for anything not supplied
    ./.azure/deploy.ps1 -ResourceGroup my-rg

.EXAMPLE
    # Fully scripted via config object
    $cfg = @{
        ResourceGroup     = 'my-rg'
        AcrRepository     = 'myorg'
        AiEndpoint        = 'https://myai.cognitiveservices.azure.com'
        SpTenantId        = '...'
        SpClientId        = '...'
        SpClientSecret    = '...'
        DbConnectionString = 'Host=...;...'
        MeisterAdminKey   = '...'
        MeisterClientKeys = '...'
        AiApiKey          = '...'
        DbUser            = 'postgres'
        DbPassword        = '...'
    }
    ./.azure/deploy.ps1 -Config $cfg
#>
[CmdletBinding()]
param(
    [hashtable]$Config,

    [string]$ResourceGroup,
    [string]$ProjectName,
    [string]$Location,
    [string]$AiEndpoint,
    [string]$AiDeploymentName,
    [string]$ImageTag,
    [string]$AcrRepository,
    [string]$SpTenantId,
    [string]$SpClientId,
    [object]$SpClientSecret,        # string or SecureString
    [object]$DbConnectionString,    # string or SecureString
    [object]$MeisterAdminKey,       # string or SecureString
    [object]$MeisterClientKeys,     # string or SecureString
    [object]$AiApiKey,              # string or SecureString
    [object]$DbUser,                # string or SecureString
    [object]$DbPassword             # string or SecureString
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ── Helpers ───────────────────────────────────────────────────────────────────

function Write-Step([string]$Message) {
    Write-Host "`n── $Message" -ForegroundColor Cyan
}

function Write-Success([string]$Message) {
    Write-Host "   ✓ $Message" -ForegroundColor Green
}

function Write-Fail([string]$Message) {
    Write-Host "   ✗ $Message" -ForegroundColor Red
}

function ConvertTo-PlainText([object]$Value) {
    if ($Value -is [SecureString]) {
        return ConvertFrom-SecureString -SecureString $Value -AsPlainText
    }
    return [string]$Value
}

function Assert-NotEmpty([object]$Value, [string]$Name) {
    $plain = ConvertTo-PlainText $Value
    if ([string]::IsNullOrWhiteSpace($plain)) {
        Write-Fail "$Name is required."
        exit 1
    }
}

# Apply a Config hashtable value to a variable only if the variable is not already set.
# Accepts both plain strings and SecureStrings from the hashtable.
function Use-Config([string]$Key, [ref]$Target, [bool]$IsSecret = $false) {
    if ($Config -and $Config.ContainsKey($Key) -and -not $Target.Value) {
        $val = $Config[$Key]
        if ($IsSecret -and $val -is [string]) {
            $Target.Value = ConvertTo-SecureString $val -AsPlainText -Force
        } else {
            $Target.Value = $val
        }
    }
}

# ── Banner ────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "  meister-propr Azure Deployment" -ForegroundColor White
Write-Host "  ────────────────────────────────" -ForegroundColor DarkGray
Write-Host ""

# ── Apply Config object ───────────────────────────────────────────────────────

Use-Config 'ResourceGroup'      ([ref]$ResourceGroup)
Use-Config 'ProjectName'        ([ref]$ProjectName)
Use-Config 'Location'           ([ref]$Location)
Use-Config 'AiEndpoint'         ([ref]$AiEndpoint)
Use-Config 'AiDeploymentName'   ([ref]$AiDeploymentName)

if (-not $ProjectName)      { $ProjectName      = 'meister-propr' }
if (-not $Location)         { $Location         = 'switzerlandnorth' }
if (-not $AiDeploymentName) { $AiDeploymentName = 'gpt-5.1-codex-mini' }
Use-Config 'ImageTag'           ([ref]$ImageTag)
Use-Config 'AcrRepository'      ([ref]$AcrRepository)
Use-Config 'SpTenantId'         ([ref]$SpTenantId)
Use-Config 'SpClientId'         ([ref]$SpClientId)
Use-Config 'SpClientSecret'     ([ref]$SpClientSecret)     -IsSecret $true
Use-Config 'DbConnectionString' ([ref]$DbConnectionString) -IsSecret $true
Use-Config 'MeisterAdminKey'    ([ref]$MeisterAdminKey)    -IsSecret $true
Use-Config 'MeisterClientKeys'  ([ref]$MeisterClientKeys)  -IsSecret $true
Use-Config 'AiApiKey'           ([ref]$AiApiKey)           -IsSecret $true
Use-Config 'DbUser'             ([ref]$DbUser)             -IsSecret $true
Use-Config 'DbPassword'         ([ref]$DbPassword)         -IsSecret $true

# ── Prerequisites ─────────────────────────────────────────────────────────────

Write-Step "Checking prerequisites"

if (-not (Get-Command 'az' -ErrorAction SilentlyContinue)) {
    Write-Fail "'az' (Azure CLI) is not installed or not on PATH."
    exit 1
}
Write-Success "az found"

$ContainerTool = $null
foreach ($tool in @('docker', 'podman')) {
    if (Get-Command $tool -ErrorAction SilentlyContinue) {
        $ContainerTool = $tool
        break
    }
}
if (-not $ContainerTool) {
    Write-Fail "Neither 'docker' nor 'podman' is installed or on PATH."
    exit 1
}
Write-Success "$ContainerTool found"

if (-not (Test-Path 'Dockerfile')) {
    Write-Fail "Script must be run from the repository root (Dockerfile not found)."
    exit 1
}
Write-Success "Running from repository root"

# ── Collect missing parameters ────────────────────────────────────────────────

Write-Step "Collecting parameters"

if (-not $ResourceGroup)      { $ResourceGroup      = Read-Host "Resource group name" }
if (-not $AiEndpoint)         { $AiEndpoint         = Read-Host "AI Services endpoint (https://....cognitiveservices.azure.com)" }
if (-not $AcrRepository)      { $AcrRepository      = Read-Host "ACR repository prefix (e.g. myorg)" }
if (-not $SpTenantId)         { $SpTenantId         = Read-Host "Service principal tenant ID" }
if (-not $SpClientId)         { $SpClientId         = Read-Host "Service principal client ID" }
if (-not $SpClientSecret)     { $SpClientSecret     = Read-Host "Service principal client secret"  -AsSecureString }
if (-not $DbConnectionString) { $DbConnectionString = Read-Host "DB connection string (Host=...;Port=5432;...)" -AsSecureString }
if (-not $MeisterAdminKey)    { $MeisterAdminKey    = Read-Host "Meister admin key (X-Admin-Key)"  -AsSecureString }
if (-not $MeisterClientKeys)  { $MeisterClientKeys  = Read-Host "Meister client keys (comma-separated)" -AsSecureString }
if (-not $AiApiKey)           { $AiApiKey           = Read-Host "Azure OpenAI API key"             -AsSecureString }
if (-not $DbUser)             { $DbUser             = Read-Host "PostgreSQL username"              -AsSecureString }
if (-not $DbPassword)         { $DbPassword         = Read-Host "PostgreSQL password"              -AsSecureString }

if (-not $ImageTag) {
    $gitTag   = git rev-parse --short HEAD 2>$null
    $ImageTag = if ($gitTag) { $gitTag } else { 'latest' }
    Write-Host "   Image tag not provided — using '$ImageTag'" -ForegroundColor DarkGray
}

# ── Validation ────────────────────────────────────────────────────────────────

Write-Step "Validating parameters"

Assert-NotEmpty $ResourceGroup      'ResourceGroup'
Assert-NotEmpty $ProjectName        'ProjectName'
Assert-NotEmpty $Location           'Location'
Assert-NotEmpty $AiEndpoint         'AiEndpoint'
Assert-NotEmpty $AiDeploymentName   'AiDeploymentName'
Assert-NotEmpty $ImageTag           'ImageTag'
Assert-NotEmpty $AcrRepository      'AcrRepository'
Assert-NotEmpty $SpTenantId         'SpTenantId'
Assert-NotEmpty $SpClientId         'SpClientId'
Assert-NotEmpty $SpClientSecret     'SpClientSecret'
Assert-NotEmpty $DbConnectionString 'DbConnectionString'
Assert-NotEmpty $MeisterAdminKey    'MeisterAdminKey'
Assert-NotEmpty $MeisterClientKeys  'MeisterClientKeys'
Assert-NotEmpty $AiApiKey           'AiApiKey'
Assert-NotEmpty $DbUser             'DbUser'
Assert-NotEmpty $DbPassword         'DbPassword'

if ($AiEndpoint -notmatch '^https://') {
    Write-Fail "AiEndpoint must start with https://"
    exit 1
}

$rgExists = az group exists --name $ResourceGroup | ConvertFrom-Json
if (-not $rgExists) {
    Write-Fail "Resource group '$ResourceGroup' does not exist."
    exit 1
}

Write-Success "All parameters valid"

# ── Derived values ────────────────────────────────────────────────────────────

$SafeName  = $ProjectName.Replace('-', '')
$AcrName   = "${SafeName}acr"
$AcrServer = "$AcrName.azurecr.io"

$BackendImage      = "$AcrServer/$AcrRepository/$ProjectName`:$ImageTag"
$AdminUiImage      = "$AcrServer/$AcrRepository/$ProjectName-admin-ui`:$ImageTag"
$ReverseProxyImage = "$AcrServer/$AcrRepository/$ProjectName-reverse-proxy`:$ImageTag"

# ── Summary ───────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "  Deployment summary" -ForegroundColor White
Write-Host "  ──────────────────────────────────────────" -ForegroundColor DarkGray
Write-Host "  Resource group  : $ResourceGroup"
Write-Host "  Project name    : $ProjectName"
Write-Host "  Location        : $Location"
Write-Host "  ACR             : $AcrServer"
Write-Host "  Image tag       : $ImageTag"
Write-Host "  AI endpoint     : $AiEndpoint"
Write-Host "  AI deployment   : $AiDeploymentName"
Write-Host "  Container tool  : $ContainerTool"
Write-Host ""

$confirm = Read-Host "Proceed? (y/N)"
if ($confirm -notmatch '^[Yy]$') {
    Write-Host "Aborted." -ForegroundColor Yellow
    exit 0
}

# ── Shared Bicep parameter builder ───────────────────────────────────────────

function Get-BicepParams([bool]$DeployApps) {
    return @(
        "projectName=$ProjectName"
        "location=$Location"
        "aiEndpoint=$AiEndpoint"
        "aiDeploymentName=$AiDeploymentName"
        "imageTag=$ImageTag"
        "acrRepository=$AcrRepository"
        "deployApps=$($DeployApps.ToString().ToLower())"
        "spTenantId=$SpTenantId"
        "spClientId=$SpClientId"
        "spClientSecret=$(ConvertTo-PlainText $SpClientSecret)"
        "dbConnectionString=$(ConvertTo-PlainText $DbConnectionString)"
        "meisterAdminKey=$(ConvertTo-PlainText $MeisterAdminKey)"
        "meisterClientKeys=$(ConvertTo-PlainText $MeisterClientKeys)"
        "aiApiKey=$(ConvertTo-PlainText $AiApiKey)"
        "dbUser=$(ConvertTo-PlainText $DbUser)"
        "dbPassword=$(ConvertTo-PlainText $DbPassword)"
    )
}

# ── Step 1: Build images ──────────────────────────────────────────────────────

Write-Step "Step 1/4 — Building images ($ContainerTool)"

Write-Host "   Building backend..." -ForegroundColor DarkGray
& $ContainerTool build -t $BackendImage -f Dockerfile .
if ($LASTEXITCODE -ne 0) { Write-Fail "Backend build failed."; exit 1 }
Write-Success "Backend: $BackendImage"

Write-Host "   Building admin-ui..." -ForegroundColor DarkGray
& $ContainerTool build -t $AdminUiImage -f admin-ui/Dockerfile .
if ($LASTEXITCODE -ne 0) { Write-Fail "Admin-ui build failed."; exit 1 }
Write-Success "Admin-ui: $AdminUiImage"

Write-Host "   Building reverse proxy..." -ForegroundColor DarkGray
& $ContainerTool build -t $ReverseProxyImage -f nginx/Dockerfile ./nginx
if ($LASTEXITCODE -ne 0) { Write-Fail "Reverse proxy build failed."; exit 1 }
Write-Success "Reverse proxy: $ReverseProxyImage"

# ── Step 2: Deploy infrastructure ────────────────────────────────────────────

Write-Step "Step 2/4 — Deploying infrastructure (deployApps=false)"

az deployment group create `
    --resource-group $ResourceGroup `
    --template-file "$PSScriptRoot/main.bicep" `
    --parameters (Get-BicepParams $false) `
    --output none

if ($LASTEXITCODE -ne 0) { Write-Fail "Infrastructure deployment failed."; exit 1 }
Write-Success "Infrastructure deployed"

# ── Step 3: Push images ───────────────────────────────────────────────────────

Write-Step "Step 3/4 — Pushing images to ACR"

if ($ContainerTool -eq 'podman') {
    # az acr login configures the Docker credential store which Podman does not use.
    # Fetch a short-lived token and pipe it via stdin to avoid shell quoting issues.
    $acrTokenJson = az acr login --name $AcrName --expose-token --output json | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { Write-Fail "ACR token fetch failed."; exit 1 }
    $acrTokenJson.accessToken | & podman login $AcrServer --username 00000000-0000-0000-0000-000000000000 --password-stdin
    if ($LASTEXITCODE -ne 0) { Write-Fail "Podman ACR login failed."; exit 1 }
} else {
    az acr login --name $AcrName
    if ($LASTEXITCODE -ne 0) { Write-Fail "ACR login failed."; exit 1 }
}

foreach ($image in @($BackendImage, $AdminUiImage, $ReverseProxyImage)) {
    Write-Host "   Pushing $image..." -ForegroundColor DarkGray
    & $ContainerTool push $image
    if ($LASTEXITCODE -ne 0) { Write-Fail "Push failed for $image"; exit 1 }
    Write-Success "Pushed $image"
}

# ── Step 4: Deploy container apps ────────────────────────────────────────────

Write-Step "Step 4/4 — Deploying container apps (deployApps=true)"

az deployment group create `
    --resource-group $ResourceGroup `
    --template-file "$PSScriptRoot/main.bicep" `
    --parameters (Get-BicepParams $true) `
    --output none

if ($LASTEXITCODE -ne 0) { Write-Fail "Container apps deployment failed."; exit 1 }
Write-Success "Container apps deployed"

# ── Done ──────────────────────────────────────────────────────────────────────

$url = az deployment group show `
    --resource-group $ResourceGroup `
    --name main `
    --query properties.outputs.reverseProxyUrl.value -o tsv

Write-Host ""
Write-Host "  Deployment complete!" -ForegroundColor Green
Write-Host "  URL: $url" -ForegroundColor White
Write-Host ""
