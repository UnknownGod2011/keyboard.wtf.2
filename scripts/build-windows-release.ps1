param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\KeyboardWtf.csproj"
$publishDir = Join-Path $root "dist\release\keyboard-wtf-win-x64"
$installerScript = Join-Path $root "installer\keyboard-wtf.iss"
$installerDir = Join-Path $root "dist\installer"
$siteDownloadDir = Join-Path $root "site\downloads"
$iscc = Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"

if (-not (Test-Path -LiteralPath $iscc)) {
    throw "Inno Setup 6 was not found at $iscc"
}

if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $installerDir -Force | Out-Null
New-Item -ItemType Directory -Path $siteDownloadDir -Force | Out-Null

dotnet publish $project `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDir `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

& $iscc $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE"
}

$installer = Join-Path $installerDir "keyboard-wtf-setup.exe"
$siteInstaller = Join-Path $siteDownloadDir "keyboard-wtf-setup.exe"
$copied = $false
for ($attempt = 1; $attempt -le 10; $attempt++) {
    try {
        Copy-Item -LiteralPath $installer -Destination $siteInstaller -Force
        $copied = $true
        break
    }
    catch {
        if ($attempt -eq 10) { throw }
        Start-Sleep -Milliseconds 750
    }
}

if (-not $copied) {
    throw "Could not copy the installer to the website download directory"
}

$hash = Get-FileHash -LiteralPath $siteInstaller -Algorithm SHA256
$sizeMb = [Math]::Round((Get-Item -LiteralPath $siteInstaller).Length / 1MB, 1)

Write-Host "Built: $siteInstaller"
Write-Host "Size: $sizeMb MB"
Write-Host "SHA256: $($hash.Hash)"
