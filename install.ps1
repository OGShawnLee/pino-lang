<#
.SYNOPSIS
Windows installer for Pino Lang.

.DESCRIPTION
Downloads the latest Pino Lang release archive for Windows x64, verifies its
SHA-256 checksum against the release's SHA256SUMS file, installs the full
folder (pino.exe, runtime-re/, runtime/, tooling/tcc/, games/, notices) to
~/.pino, adds ~/.pino to the user PATH, and removes the legacy 0.3 ~/.pino/bin
PATH entry if present.

.PARAMETER FromFile
Optional path to a local release zip archive (for offline or local testing).
When omitted, downloads the latest release from GitHub.

.PARAMETER InstallDir
Optional target installation directory. Defaults to "$HOME\.pino".

.PARAMETER Sha256Sums
Optional path to a local SHA256SUMS file to verify -FromFile against. If
omitted with -FromFile, a sibling SHA256SUMS file next to the archive is
checked when present.

.PARAMETER NoPathUpdate
Skip modifying the persistent User PATH variable (still prints install status).
#>
param(
    [string]$FromFile = "",
    [string]$InstallDir = "",
    [string]$Sha256Sums = "",
    [switch]$NoPathUpdate
)

$ErrorActionPreference = 'Stop'

function Get-HostArchitecture {
    try {
        return [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    } catch {
        switch ($env:PROCESSOR_ARCHITECTURE) {
            "AMD64" { return "x64" }
            "ARM64" { return "arm64" }
            default {
                if ($env:PROCESSOR_ARCHITECTURE) { return $env:PROCESSOR_ARCHITECTURE.ToLowerInvariant() }
                return "unknown"
            }
        }
    }
}

function Normalize-PathEntry([string]$Entry) {
    if ([string]::IsNullOrWhiteSpace($Entry)) { return "" }
    return $Entry.Trim().TrimEnd('\', '/')
}

function Verify-Checksum([string]$ArchivePath, [string]$AssetName, [string]$SumsFilePath) {
    $ExpectedHash = $null
    foreach ($Line in (Get-Content -Path $SumsFilePath)) {
        $Trimmed = $Line.Trim()
        if ([string]::IsNullOrEmpty($Trimmed) -or $Trimmed.StartsWith('#')) {
            continue
        }
        if ($Trimmed -match '^([0-9a-fA-F]{64})\s+\*?(.+)$') {
            $Hash = $Matches[1].ToLowerInvariant()
            $Name = $Matches[2].Trim()
            if ($Name -eq $AssetName -or [System.IO.Path]::GetFileName($Name) -eq $AssetName) {
                $ExpectedHash = $Hash
                break
            }
        }
    }

    if (-not $ExpectedHash) {
        throw "Could not find a checksum entry for '$AssetName' in '$SumsFilePath'."
    }

    $ActualHash = (Get-FileHash -Path $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($ActualHash -ne $ExpectedHash) {
        throw "SHA-256 verification failed for '$AssetName': expected $ExpectedHash, got $ActualHash."
    }
}

$Arch = Get-HostArchitecture
if ($Arch -ne "x64") {
    throw "Unsupported Windows architecture '$Arch'. Precompiled Pino binaries are currently available for Windows x64."
}

$AssetName = "pino-windows-x64.zip"
if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    $InstallDir = Join-Path $HOME ".pino"
}
$InstallDir = [System.IO.Path]::GetFullPath($InstallDir)
$LegacyBinDir = Join-Path $InstallDir "bin"

$TempWork = Join-Path ([System.IO.Path]::GetTempPath()) ("pino-install-" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $TempWork | Out-Null

try {
    $InstalledLabel = ""
    $ArchivePath = ""

    if (-not [string]::IsNullOrWhiteSpace($FromFile)) {
        if (-not (Test-Path -LiteralPath $FromFile)) {
            throw "Local archive not found: '$FromFile'."
        }
        $ArchivePath = (Resolve-Path -LiteralPath $FromFile).Path
        $LocalFileName = [System.IO.Path]::GetFileName($ArchivePath)
        $InstalledLabel = "from $ArchivePath"

        if (-not [string]::IsNullOrWhiteSpace($Sha256Sums)) {
            if (-not (Test-Path -LiteralPath $Sha256Sums)) {
                throw "Checksum file not found: '$Sha256Sums'."
            }
            Write-Host "Verifying archive checksum..."
            Verify-Checksum -ArchivePath $ArchivePath -AssetName $LocalFileName -SumsFilePath $Sha256Sums
        } else {
            $SiblingSums = Join-Path (Split-Path -Parent $ArchivePath) "SHA256SUMS"
            if (Test-Path -LiteralPath $SiblingSums) {
                Write-Host "Verifying archive checksum against $SiblingSums..."
                Verify-Checksum -ArchivePath $ArchivePath -AssetName $LocalFileName -SumsFilePath $SiblingSums
            }
        }
    } else {
        Write-Host "Installing Pino Lang for Windows ($Arch)..."
        $Repo = "OGShawnLee/pino-lang"
        $ReleaseUrl = "https://api.github.com/repos/$Repo/releases/latest"
        $Headers = @{ "User-Agent" = "Pino-Installer/0.4" }

        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $Release = Invoke-RestMethod -Uri $ReleaseUrl -Headers $Headers -UseBasicParsing
        $TagName = $Release.tag_name
        if ([string]::IsNullOrWhiteSpace($TagName)) {
            throw "Could not determine the latest Pino release tag from GitHub."
        }

        $Asset = $Release.assets | Where-Object { $_.name -eq $AssetName } | Select-Object -First 1
        if (-not $Asset) {
            throw "Could not find release asset '$AssetName' in release '$TagName'."
        }

        $SumsAsset = $Release.assets | Where-Object { $_.name -eq "SHA256SUMS" } | Select-Object -First 1
        if (-not $SumsAsset) {
            throw "Could not find 'SHA256SUMS' in release '$TagName'; refusing unverified install."
        }

        $ArchivePath = Join-Path $TempWork $AssetName
        $SumsPath = Join-Path $TempWork "SHA256SUMS"

        Write-Host "Downloading Pino $TagName ($AssetName)..."
        Invoke-WebRequest -Uri $Asset.browser_download_url -Headers $Headers -OutFile $ArchivePath -UseBasicParsing
        Invoke-WebRequest -Uri $SumsAsset.browser_download_url -Headers $Headers -OutFile $SumsPath -UseBasicParsing

        Write-Host "Verifying SHA-256 checksum..."
        Verify-Checksum -ArchivePath $ArchivePath -AssetName $AssetName -SumsFilePath $SumsPath
        $InstalledLabel = $TagName
    }

    $ExtractDir = Join-Path $TempWork "extract"
    New-Item -ItemType Directory -Force -Path $ExtractDir | Out-Null
    Expand-Archive -LiteralPath $ArchivePath -DestinationPath $ExtractDir -Force

    $ExeItem = Get-ChildItem -Path $ExtractDir -Filter "pino.exe" -File -Recurse | Select-Object -First 1
    if (-not $ExeItem) {
        throw "Could not find pino.exe inside the archive."
    }
    $SourceRoot = $ExeItem.Directory.FullName

    Write-Host "Installing to $InstallDir..."
    if (Test-Path -LiteralPath $InstallDir) {
        Get-ChildItem -LiteralPath $InstallDir -Force | Remove-Item -Recurse -Force
    } else {
        New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
    }

    Get-ChildItem -LiteralPath $SourceRoot -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $InstallDir $_.Name) -Recurse -Force
    }

    if (-not $NoPathUpdate) {
        $NormInstall = Normalize-PathEntry $InstallDir
        $NormLegacyBin = Normalize-PathEntry $LegacyBinDir

        $RawUserPath = [System.Environment]::GetEnvironmentVariable("PATH", "User")
        $Entries = @()
        if (-not [string]::IsNullOrEmpty($RawUserPath)) {
            $Entries = $RawUserPath -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        }

        $Cleaned = @()
        $HasInstallDir = $false
        $RemovedLegacy = $false
        foreach ($Entry in $Entries) {
            $Norm = Normalize-PathEntry $Entry
            if ($Norm.Equals($NormLegacyBin, [System.StringComparison]::OrdinalIgnoreCase)) {
                $RemovedLegacy = $true
                continue
            }
            if ($Norm.Equals($NormInstall, [System.StringComparison]::OrdinalIgnoreCase)) {
                if (-not $HasInstallDir) {
                    $Cleaned += $InstallDir
                    $HasInstallDir = $true
                }
                continue
            }
            $Cleaned += $Entry
        }

        if (-not $HasInstallDir) {
            $Cleaned += $InstallDir
        }

        if ($RemovedLegacy -or (-not $HasInstallDir)) {
            $UpdatedUserPath = $Cleaned -join ';'
            [System.Environment]::SetEnvironmentVariable("PATH", $UpdatedUserPath, "User")
            if ($RemovedLegacy) {
                Write-Host "Removed legacy PATH entry: $LegacyBinDir"
            }
            if (-not $HasInstallDir) {
                Write-Host "Added $InstallDir to your user PATH."
            }
        }

        # Update current session PATH too.
        $SessionEntries = ($env:PATH -split ';') | Where-Object {
            -not [string]::IsNullOrWhiteSpace($_) -and
            -not (Normalize-PathEntry $_).Equals($NormLegacyBin, [System.StringComparison]::OrdinalIgnoreCase)
        }
        if (-not ($SessionEntries | Where-Object { (Normalize-PathEntry $_).Equals($NormInstall, [System.StringComparison]::OrdinalIgnoreCase) })) {
            $env:PATH = "$InstallDir;" + ($SessionEntries -join ';')
        } else {
            $env:PATH = $SessionEntries -join ';'
        }
    }

    $InstalledVersion = & (Join-Path $InstallDir "pino.exe") version 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($InstalledVersion)) {
        Write-Host "Installed $InstalledVersion to $InstallDir ($InstalledLabel)."
    } else {
        Write-Host "Installed Pino ($InstalledLabel) to $InstallDir."
    }
    Write-Host "Run 'pino init hello' to start."
} finally {
    if (Test-Path -LiteralPath $TempWork) {
        Remove-Item -Recurse -Force -LiteralPath $TempWork -ErrorAction SilentlyContinue
    }
}
