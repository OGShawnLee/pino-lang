# install.ps1 - Windows installer for Pino Lang
$ErrorActionPreference = 'Stop'

# Define path
$InstallDir = Join-Path $HOME ".pino"
$BinDir = Join-Path $InstallDir "bin"

# Create directories
if (-not (Test-Path $BinDir)) {
    New-Item -ItemType Directory -Path $BinDir | Out-Null
}

Write-Host "🌲 Installing Pino Lang..." -ForegroundColor Green

# Fetch latest release metadata from GitHub
$Repo = "OGShawnLee/pino-lang"
$ReleaseUrl = "https://api.github.com/repos/$Repo/releases/latest"

try {
    # Force TLS 1.2
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $Release = Invoke-RestMethod -Uri $ReleaseUrl -UseBasicParsing
} catch {
    Write-Error "Failed to fetch latest release from GitHub: $_"
}

# Locate Windows asset
$Asset = $Release.assets | Where-Object { $_.name -like "*windows*.zip" } | Select-Object -First 1
if (-not $Asset) {
    Write-Error "Could not find Windows asset in the latest release."
}

$DownloadUrl = $Asset.browser_download_url
$TempZip = Join-Path $env:TEMP "pino-install.zip"

Write-Host "Downloading version $($Release.tag_name) from $DownloadUrl..." -ForegroundColor Cyan
Invoke-WebRequest -Uri $DownloadUrl -OutFile $TempZip -UseBasicParsing

# Extract
Write-Host "Extracting files to $BinDir..." -ForegroundColor Cyan
$TempExtract = Join-Path $env:TEMP "pino-extract"
if (Test-Path $TempExtract) {
    Remove-Item -Recurse -Force $TempExtract
}
Expand-Archive -Path $TempZip -DestinationPath $TempExtract

# Copy binary
$ExePath = Join-Path $TempExtract "pino.exe"
if (-not (Test-Path $ExePath)) {
    $ExePath = Get-ChildItem -Path $TempExtract -Filter "pino*.exe" -Recurse | Select-Object -First 1
}

if (-not $ExePath) {
    Write-Error "Could not find pino.exe inside the downloaded archive."
}

Copy-Item -Path $ExePath -Destination (Join-Path $BinDir "pino.exe") -Force

# Clean up
Remove-Item -Force $TempZip
Remove-Item -Recurse -Force $TempExtract | Out-Null

# Add to PATH permanently
$UserPath = [System.Environment]::GetEnvironmentVariable("PATH", "User")
if ($UserPath -split ";" -notcontains $BinDir) {
    Write-Host "Adding $BinDir to your user PATH variable..." -ForegroundColor Cyan
    $NewPath = "$UserPath;$BinDir"
    [System.Environment]::SetEnvironmentVariable("PATH", $NewPath, "User")
    # Also update current session's PATH
    $env:PATH = "$env:PATH;$BinDir"
}

Write-Host "🌲 Pino Lang ($($Release.tag_name)) has been successfully installed!" -ForegroundColor Green
Write-Host "Restart your terminal and type 'pino' to get started." -ForegroundColor Yellow
