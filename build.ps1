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
            (Join-Path $Root "tools\nsis\makensis.exe"),
            "${env:ProgramFiles(x86)}\NSIS\makensis.exe",
            "${env:ProgramFiles}\NSIS\makensis.exe",
            "$env:LOCALAPPDATA\Programs\NSIS\makensis.exe",
            "$env:USERPROFILE\scoop\apps\nsis\current\makensis.exe"
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

function Install-NsisViaWinget {
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $winget) {
        return $false
    }

    Write-Host "==> Installing NSIS via winget..." -ForegroundColor Yellow
    & winget install --id NSIS.NSIS -e --silent `
        --accept-package-agreements `
        --accept-source-agreements
    # winget may return non-zero when already installed; keep going and re-scan.
    return $true
}

function Install-NsisViaScoop {
    $scoop = Get-Command scoop -ErrorAction SilentlyContinue
    if (-not $scoop) {
        return $false
    }

    Write-Host "==> Installing NSIS via scoop..." -ForegroundColor Yellow
    & scoop install nsis
    return $true
}

function Install-PortableNsis {
    $toolsDir = Join-Path $Root "tools\nsis"
    $makensisLocal = Join-Path $toolsDir "makensis.exe"
    if (Test-Path -LiteralPath $makensisLocal) {
        return $makensisLocal
    }

    Write-Host "==> Downloading portable NSIS..." -ForegroundColor Yellow

    $version = "3.10"
    $zipPath = Join-Path $env:TEMP "nsis-$version.zip"
    $extractRoot = Join-Path $env:TEMP "nsis-$version-extract"
    $urls = @(
        "https://github.com/kichik/nsis/releases/download/v$version/nsis-$version.zip",
        "https://mirror.ghproxy.com/https://github.com/kichik/nsis/releases/download/v$version/nsis-$version.zip",
        "https://downloads.sourceforge.net/project/nsis/NSIS%203/$version/nsis-$version.zip"
    )

    $downloaded = $false
    foreach ($url in $urls) {
        try {
            Write-Host "    $url"
            Invoke-WebRequest -Uri $url -OutFile $zipPath -UseBasicParsing -MaximumRedirection 10
            if ((Test-Path -LiteralPath $zipPath) -and ((Get-Item -LiteralPath $zipPath).Length -gt 1MB)) {
                $downloaded = $true
                break
            }
        }
        catch {
            Write-Host "    download failed: $($_.Exception.Message)" -ForegroundColor DarkYellow
        }
    }

    if (-not $downloaded) {
        return $null
    }

    if (Test-Path -LiteralPath $extractRoot) {
        Remove-Item -Recurse -Force $extractRoot
    }
    New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null
    Expand-Archive -LiteralPath $zipPath -DestinationPath $extractRoot -Force

    $extractedMakensis = Get-ChildItem -Path $extractRoot -Filter makensis.exe -Recurse -File |
        Select-Object -First 1
    if (-not $extractedMakensis) {
        return $null
    }

    $extractedDir = $extractedMakensis.Directory.FullName
    if (Test-Path -LiteralPath $toolsDir) {
        Remove-Item -Recurse -Force $toolsDir
    }
    New-Item -ItemType Directory -Force -Path (Split-Path $toolsDir -Parent) | Out-Null
    Copy-Item -Recurse -Force $extractedDir $toolsDir

    if (Test-Path -LiteralPath $makensisLocal) {
        Write-Host "==> Portable NSIS ready: $makensisLocal" -ForegroundColor Green
        return $makensisLocal
    }
    return $null
}

function Resolve-MakeNsis {
    $found = Find-MakeNsis
    if ($found) {
        return $found
    }

    Write-Host "==> NSIS not found; attempting automatic install (scoop -> winget -> portable)..." -ForegroundColor Yellow

    if (Install-NsisViaScoop) {
        $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "User") + ";" +
                    [System.Environment]::GetEnvironmentVariable("Path", "Machine")
        $found = Find-MakeNsis
        if ($found) { return $found }
    }

    if (Install-NsisViaWinget) {
        # winget often needs a refreshed PATH / known install dirs
        $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
                    [System.Environment]::GetEnvironmentVariable("Path", "User")
        $found = Find-MakeNsis
        if ($found) { return $found }
    }

    $portable = Install-PortableNsis
    if ($portable) {
        return $portable
    }

    throw @"
makensis.exe not found and automatic install failed.
Install NSIS manually, then re-run:
  winget install --id NSIS.NSIS -e --accept-package-agreements --accept-source-agreements
"@
}

if (-not $Version) {
    $Version = Get-DefaultVersion
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must be semver like 1.2.3 (got '$Version')"
}

$project = Join-Path $Root "src\MovieOverlay\MovieOverlay.csproj"
$publishDir = Join-Path $Root "publish\app"
$distDir = Join-Path $Root "dist"
$nsi = Join-Path $Root "installer\MovieOverlay.nsi"
$setupName = "MovieOverlay-Setup-$Version.exe"
$setupPath = Join-Path $distDir $setupName

Write-Host "==> Version: $Version" -ForegroundColor Cyan
Write-Host "==> Cleaning publish/dist output" -ForegroundColor Cyan
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

$makensis = Resolve-MakeNsis
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
