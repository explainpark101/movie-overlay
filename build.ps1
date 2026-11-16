#Requires -Version 5.1
<#
.SYNOPSIS
  Publish Movie Overlay and build an NSIS installer.

.EXAMPLE
  .\build.ps1
  .\build.ps1 -Version 1.2.3
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipNsis
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Root

function Get-DefaultVersion {
    $versionFile = Join-Path $Root "VERSION"
    if (Test-Path $versionFile) {
        $raw = (Get-Content -Path $versionFile -Raw).Trim()
        if ($raw -match '^\d+\.\d+\.\d+$') {
            return $raw
        }
    }
    return "0.1.0"
}

function Find-MakeNsis {
    $candidates = [System.Collections.Generic.List[string]]::new()
    $fromPath = Get-Command makensis -ErrorAction SilentlyContinue
    if ($fromPath -and $fromPath.Source) {
        $candidates.Add($fromPath.Source)
    }
    foreach ($path in @(
            "${env:ProgramFiles(x86)}\NSIS\makensis.exe",
            "${env:ProgramFiles}\NSIS\makensis.exe",
            "$env:LOCALAPPDATA\Programs\NSIS\makensis.exe"
        )) {
        if ($path -and (Test-Path -LiteralPath $path)) {
            $candidates.Add($path)
        }
    }

    if ($candidates.Count -gt 0) {
        return $candidates[0]
    }
    return $null
}

if (-not $Version) {
    $Version = Get-DefaultVersion
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must be semver like 1.2.3 (got '$Version')"
}

$project = Join-Path $Root "src\MovieOverlay\MovieOverlay.csproj"
$publishDir = Join-Path $Root "publish\app"
$distDir = Join-Path $Root "publish"
$nsi = Join-Path $Root "installer\MovieOverlay.nsi"
$setupName = "MovieOverlay-Setup-$Version.exe"
$setupPath = Join-Path $distDir $setupName

Write-Host "==> Version: $Version" -ForegroundColor Cyan
Write-Host "==> Cleaning publish output" -ForegroundColor Cyan
if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

Write-Host "==> dotnet publish ($Configuration / $Runtime)" -ForegroundColor Cyan
& dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version=$Version `
    -p:AssemblyVersion=$Version `
    -p:FileVersion=$Version `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$exePath = Join-Path $publishDir "MovieOverlay.exe"
if (-not (Test-Path $exePath)) {
    throw "Published EXE not found: $exePath"
}

if ($SkipNsis) {
    Write-Host "==> SkipNsis set; publish complete at $publishDir" -ForegroundColor Yellow
    exit 0
}

$makensis = Find-MakeNsis
if (-not $makensis) {
    throw @"
makensis.exe not found. Install NSIS first, then re-run:
  winget install --id NSIS.NSIS -e --accept-package-agreements --accept-source-agreements
"@
}

Write-Host "==> NSIS: $makensis" -ForegroundColor Cyan
if (Test-Path $setupPath) {
    Remove-Item -Force $setupPath
}

# NSIS paths: use forward slashes for reliability
$publishDirNsis = ($publishDir -replace '\\', '/')
$outFileNsis = ($setupPath -replace '\\', '/')
$iconPath = Join-Path $Root "assets\app.ico"
if (-not (Test-Path -LiteralPath $iconPath)) {
    throw "Installer icon missing: $iconPath (expected from assets\icon_1000.png)"
}
$iconPathNsis = ($iconPath -replace '\\', '/')

& $makensis `
    "/DAPP_VERSION=$Version" `
    "/DPUBLISH_DIR=$publishDirNsis" `
    "/DOUT_FILE=$outFileNsis" `
    "/DAPP_ICON=$iconPathNsis" `
    $nsi

if ($LASTEXITCODE -ne 0) {
    throw "makensis failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $setupPath)) {
    throw "Installer was not created: $setupPath"
}

Write-Host "==> Done" -ForegroundColor Green
Write-Host "    App:       $exePath"
Write-Host "    Installer: $setupPath"
