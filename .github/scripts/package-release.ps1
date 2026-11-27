<#
.SYNOPSIS
Packages the Windows x64 release archive (pino-windows-x64.zip) for Pino Lang.

.DESCRIPTION
Publishes Pino.Cli as a NativeAOT binary (or a self-contained single-file binary
when -NoAot is passed), assembles the release folder layout (pino.exe,
runtime-re/*.c|*.h, tooling/tcc/, games/*.pino, LICENSE, THIRD_PARTY_NOTICES.md),
and compresses it into pino-windows-x64.zip.
#>
param(
    [string]$Rid = "win-x64",
    [string]$ArtifactName = "pino-windows-x64",
    [string]$OutputDir = ".",
    [string]$TccDir = "tooling/tcc",
    [switch]$NoAot,
    [switch]$NoTrim
)

$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$ResolvedOutputDir = if ([System.IO.Path]::IsPathRooted($OutputDir)) {
    $OutputDir
} else {
    Join-Path (Get-Location).Path $OutputDir
}
New-Item -ItemType Directory -Force -Path $ResolvedOutputDir | Out-Null
$ResolvedOutputDir = (Resolve-Path $ResolvedOutputDir).Path

$ResolvedTccDir = if ([System.IO.Path]::IsPathRooted($TccDir)) {
    $TccDir
} else {
    Join-Path $RepoRoot $TccDir
}

if (-not (Test-Path (Join-Path $ResolvedTccDir "tcc.exe"))) {
    throw "TinyCC was not found at '$ResolvedTccDir\tcc.exe'."
}

$WorkDir = Join-Path ([System.IO.Path]::GetTempPath()) ("pino-package-" + [System.Guid]::NewGuid().ToString("N"))
$PublishDir = Join-Path $WorkDir "publish"
$StageRoot = Join-Path $WorkDir $ArtifactName
New-Item -ItemType Directory -Force -Path $PublishDir, $StageRoot | Out-Null

try {
    $CliProject = Join-Path $RepoRoot "pino-csharp-re\Pino.Cli\Pino.Cli.csproj"
    if ($NoAot) {
        $PublishArgs = @(
            "publish", $CliProject,
            "-c", "Release",
            "-r", $Rid,
            "--self-contained", "true",
            "-p:PublishSingleFile=true",
            "-p:IncludeNativeLibrariesForSelfExtract=true",
            "-p:DebugType=none",
            "-p:DebugSymbols=false",
            "-o", $PublishDir
        )
        if (-not $NoTrim) {
            $PublishArgs += "-p:PublishTrimmed=true"
        }
    } else {
        # Visual Studio BuildTools' vcvarsall.bat may invoke vswhere.exe without a full path,
        # which can pollute ILCompiler's findvcvarsall.bat stdout/stderr capture when missing from PATH.
        $VsInstallerDir = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer"
        if ((Test-Path (Join-Path $VsInstallerDir "vswhere.exe")) -and ($env:PATH -notlike "*$VsInstallerDir*")) {
            $env:PATH = "$VsInstallerDir;$env:PATH"
        }

        $PublishArgs = @(
            "publish", $CliProject,
            "-c", "Release",
            "-r", $Rid,
            "-p:PublishAot=true",
            "-p:StripSymbols=true",
            "-p:OptimizationPreference=Speed",
            "-p:DebugType=none",
            "-p:DebugSymbols=false",
            "-p:WarningsNotAsErrors=NU1903",
            "-o", $PublishDir
        )
    }

    Write-Host "Publishing Pino.Cli for $Rid ($(if ($NoAot) { 'single-file' } else { 'NativeAOT' }))..."
    & dotnet @PublishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    $PublishedExe = Join-Path $PublishDir "pino.exe"
    if (-not (Test-Path $PublishedExe)) {
        throw "Expected published binary at '$PublishedExe' was not found."
    }

    Copy-Item -Path $PublishedExe -Destination (Join-Path $StageRoot "pino.exe")

    # runtime-re/*.c and *.h (excluding runtime-re/tests/)
    $StageRuntimeRe = Join-Path $StageRoot "runtime-re"
    New-Item -ItemType Directory -Force -Path $StageRuntimeRe | Out-Null
    $RuntimeReFiles = Get-ChildItem -Path (Join-Path $RepoRoot "runtime-re") -File | Where-Object { $_.Extension -in @(".c", ".h") }
    if ($RuntimeReFiles.Count -eq 0) {
        throw "No .c/.h files found under runtime-re/."
    }
    foreach ($File in $RuntimeReFiles) {
        Copy-Item -Path $File.FullName -Destination (Join-Path $StageRuntimeRe $File.Name)
    }

    # tooling/tcc/
    $StageTooling = Join-Path $StageRoot "tooling"
    New-Item -ItemType Directory -Force -Path $StageTooling | Out-Null
    Copy-Item -Recurse -Force -Path $ResolvedTccDir -Destination (Join-Path $StageTooling "tcc")

    # games/*.pino only
    $StageGames = Join-Path $StageRoot "games"
    New-Item -ItemType Directory -Force -Path $StageGames | Out-Null
    $GameFiles = Get-ChildItem -Path (Join-Path $RepoRoot "games") -Filter "*.pino" -File
    if ($GameFiles.Count -eq 0) {
        throw "No .pino games found under games/."
    }
    foreach ($Game in $GameFiles) {
        Copy-Item -Path $Game.FullName -Destination (Join-Path $StageGames $Game.Name)
    }

    # Notices and license
    $NoticesSrc = Join-Path $RepoRoot "THIRD_PARTY_NOTICES.md"
    if (-not (Test-Path $NoticesSrc)) {
        throw "THIRD_PARTY_NOTICES.md is missing at repo root."
    }
    Copy-Item -Path $NoticesSrc -Destination (Join-Path $StageRoot "THIRD_PARTY_NOTICES.md")

    $LicenseSrc = Join-Path $RepoRoot "LICENSE"
    if (-not (Test-Path $LicenseSrc)) {
        throw "LICENSE is missing at repo root."
    }
    Copy-Item -Path $LicenseSrc -Destination (Join-Path $StageRoot "LICENSE")

    $ArchivePath = Join-Path $ResolvedOutputDir "$ArtifactName.zip"
    if (Test-Path $ArchivePath) {
        Remove-Item -Force $ArchivePath
    }

    Write-Host "Creating archive $ArchivePath..."
    Compress-Archive -Path $StageRoot -DestinationPath $ArchivePath -CompressionLevel Optimal

    $ExeSize = (Get-Item (Join-Path $StageRoot "pino.exe")).Length
    $ZipSize = (Get-Item $ArchivePath).Length
    Write-Host "Packaged $ArchivePath ($ZipSize bytes; pino.exe $ExeSize bytes)."
} finally {
    if (Test-Path $WorkDir) {
        Remove-Item -Recurse -Force $WorkDir -ErrorAction SilentlyContinue
    }
}
