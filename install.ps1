<#
.SYNOPSIS
    Installs the Copilot Booster Launcher.

.DESCRIPTION
    Builds the CopilotBooster.exe and optionally sets the default work directory.
    Settings (allowed tools, directories, IDEs) are managed via the Settings UI in the app itself.

.PARAMETER PublishDir
    Where to publish the built exe. Default: <repo>\publish

.PARAMETER WorkDir
    Default working directory for new sessions. Saved to launcher settings.
#>

param(
    [string]$PublishDir = (Join-Path $PSScriptRoot "publish"),
    [string]$WorkDir = ""
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot
$SrcDir = Join-Path $RepoRoot "src"

# 1. Check prerequisites
Write-Host "Checking prerequisites..." -ForegroundColor Cyan
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error ".NET SDK is required. Install from https://dot.net/download"
    return
}
if (-not (Get-Command copilot.exe -ErrorAction SilentlyContinue)) {
    Write-Error "GitHub Copilot CLI is required. Install via: winget install GitHub.Copilot"
    return
}

# 2. Build
Write-Host "Building CopilotBooster..." -ForegroundColor Cyan
Push-Location $SrcDir
dotnet publish -c Release -o $PublishDir --nologo -v quiet
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    Write-Error "Build failed"
    return
}
Pop-Location
Write-Host "Published to: $PublishDir" -ForegroundColor Green

# 3. Build and run installer
Write-Host "Building installer..." -ForegroundColor Cyan
$issFile = Join-Path $RepoRoot "installer.iss"
& iscc $issFile /Q
if ($LASTEXITCODE -ne 0) {
    Write-Error "Installer build failed"
    return
}
$setupExe = Join-Path $RepoRoot "installer-output\CopilotBooster-Setup.exe"
Write-Host "Running installer..." -ForegroundColor Cyan
Start-Process -FilePath $setupExe
Write-Host "Installer completed." -ForegroundColor Green

# 4. Initialize settings if work dir provided
if ($WorkDir) {
    $settingsFile = Join-Path $env:USERPROFILE ".copilot\launcher-settings.json"
    if (Test-Path $settingsFile) {
        $settings = Get-Content $settingsFile -Raw | ConvertFrom-Json
        $settings.defaultWorkDir = $WorkDir
    } else {
        $settings = @{
            allowedTools = @()
            allowedDirs = @()
            defaultWorkDir = $WorkDir
            ides = @()
        }
    }
    $settingsDir = Split-Path $settingsFile
    if (-not (Test-Path $settingsDir)) { New-Item -ItemType Directory -Path $settingsDir -Force | Out-Null }
    $settings | ConvertTo-Json -Depth 10 | Set-Content $settingsFile
    Write-Host "Default work dir set to: $WorkDir" -ForegroundColor Green
}

# 5. Summary
Write-Host ""
Write-Host "=== Installation Complete ===" -ForegroundColor Green
Write-Host "Executable:  $PublishDir\CopilotBooster.exe"
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Run CopilotBooster.exe"
Write-Host "  2. Right-click its taskbar icon and select 'Pin to taskbar'"
Write-Host "  3. Right-click the pinned icon to access jump list:"
Write-Host "     - New Copilot Session"
Write-Host "     - Existing Sessions"
Write-Host "     - Settings (configure allowed tools, directories, IDEs)"
Write-Host ""
Write-Host "To configure settings:" -ForegroundColor Yellow
Write-Host "  Right-click pinned icon → Settings"
Write-Host "  Or run: CopilotBooster.exe --settings"
