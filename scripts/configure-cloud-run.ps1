param(
    [string]$ProjectId = "keyboard-wtf-agent",
    [string]$Region = "asia-south1",
    [string]$ServiceName = "keyboard-wtf-agent"
)

# gcloud writes normal progress messages to stderr on Windows. Check its exit
# code explicitly so a missing secret can be created instead of terminating here.
$ErrorActionPreference = "Continue"

function Resolve-Gcloud {
    $command = Get-Command gcloud -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $installed = Join-Path $env:LOCALAPPDATA "Google\Cloud SDK\google-cloud-sdk\bin\gcloud.cmd"
    if (Test-Path -LiteralPath $installed) { return $installed }

    throw "Google Cloud CLI was not found. Install it, authenticate, and rerun this script."
}

function Import-LocalEnvironment {
    $path = Join-Path $PSScriptRoot "..\.env.local"
    if (-not (Test-Path -LiteralPath $path)) { return }

    foreach ($line in Get-Content -LiteralPath $path) {
        if ($line -notmatch "^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)\s*$") { continue }
        $name = $matches[1]
        $value = $matches[2].Trim().Trim('"').Trim("'")
        if (-not [Environment]::GetEnvironmentVariable($name, "Process") -and $value) {
            [Environment]::SetEnvironmentVariable($name, $value, "Process")
        }
    }
}

function Read-SecretValue([string]$Name) {
    $existing = [Environment]::GetEnvironmentVariable($Name, "Process")
    if ($existing) { return $existing }

    Write-Host "Enter $Name (hidden input):" -ForegroundColor Cyan
    $secure = Read-Host -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

function Ensure-Secret([string]$Name, [string]$Value, [string]$RuntimeServiceAccount) {
    & $script:Gcloud secrets describe $Name --project $ProjectId *> $null
    if ($LASTEXITCODE -ne 0) {
        & $script:Gcloud secrets create $Name --project $ProjectId --replication-policy automatic --quiet | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Could not create Secret Manager secret $Name." }
    }

    $temp = [IO.Path]::GetTempFileName()
    try {
        [IO.File]::WriteAllText($temp, $Value, [Text.UTF8Encoding]::new($false))
        & $script:Gcloud secrets versions add $Name --project $ProjectId --data-file $temp --quiet | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Could not add a version for $Name." }
    }
    finally {
        Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue
        $Value = $null
    }

    & $script:Gcloud secrets add-iam-policy-binding $Name `
        --project $ProjectId `
        --member "serviceAccount:$RuntimeServiceAccount" `
        --role roles/secretmanager.secretAccessor `
        --quiet *> $null
    if ($LASTEXITCODE -ne 0) { throw "Could not grant Secret Manager access for $Name." }
}

Import-LocalEnvironment
$script:Gcloud = Resolve-Gcloud

& $Gcloud config set project $ProjectId | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Could not select Google Cloud project $ProjectId." }

$runtimeServiceAccount = (& $Gcloud run services describe $ServiceName `
    --project $ProjectId `
    --region $Region `
    --format "value(spec.template.spec.serviceAccountName)").Trim()
if (-not $runtimeServiceAccount) {
    throw "Could not resolve the runtime service account for the existing Cloud Run service."
}

foreach ($name in "GEMINI_API_KEY", "ELASTICSEARCH_API_KEY", "ELASTIC_MCP_API_KEY") {
    $value = Read-SecretValue $name
    try { Ensure-Secret $name $value $runtimeServiceAccount }
    finally { $value = $null }
}

$envVars = @(
    "GOOGLE_CLOUD_PROJECT_ID=keyboard-wtf-agent",
    "GOOGLE_CLOUD_LOCATION=asia-south1",
    "GOOGLE_CLOUD_RUN_REGION=asia-south1",
    "GOOGLE_CLOUD_RUN_SERVICE_NAME=keyboard-wtf-agent",
    "GOOGLE_GENAI_USE_VERTEXAI=true",
    "GEMINI_VERTEX_LOCATION=global",
    "GEMINI_MODEL=gemini-3.1-pro-preview",
    "ELASTICSEARCH_URL=https://my-elasticsearch-project-f21be9.es.asia-south1.gcp.elastic.cloud:443",
    "KIBANA_URL=https://my-elasticsearch-project-f21be9.kb.asia-south1.gcp.elastic.cloud",
    "ELASTIC_AGENT_BUILDER_MCP_URL=https://my-elasticsearch-project-f21be9.kb.asia-south1.gcp.elastic.cloud/api/agent_builder/mcp",
    "ELASTIC_INDEX_MEMORIES=keyboard_wtf_memories",
    "ELASTIC_INDEX_ACTIONS=keyboard_wtf_actions",
    "ELASTIC_INDEX_CHATS=keyboard_wtf_chats",
    "ELASTIC_INDEX_FAILURES=keyboard_wtf_failures",
    "DEFAULT_USER_ID=tanushshah2006",
    "DEFAULT_DEVICE_ID=tanush-windows-demo",
    "LOCAL_BRIDGE_PORT=8787",
    "WEB_DASHBOARD_ORIGIN=https://keyboard-wtf-agent-866230084016.asia-south1.run.app",
    "ENABLE_ADMIN_ROUTES=false"
) -join ","

& $Gcloud run deploy $ServiceName `
    --source (Resolve-Path (Join-Path $PSScriptRoot "..")).Path `
    --project $ProjectId `
    --region $Region `
    --allow-unauthenticated `
    --set-env-vars $envVars `
    --set-secrets "GEMINI_API_KEY=GEMINI_API_KEY:latest,ELASTICSEARCH_API_KEY=ELASTICSEARCH_API_KEY:latest,ELASTIC_MCP_API_KEY=ELASTIC_MCP_API_KEY:latest" `
    --quiet | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Cloud Run deployment failed." }

& $Gcloud run services describe $ServiceName --project $ProjectId --region $Region --format "value(status.url)"
