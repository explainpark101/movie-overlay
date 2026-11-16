<img src="assets/icon_1000.png" width="64" height="64" alt="">

# Movie Overlay

Windows tray utility that blacks out selected monitors after the mouse stays still, so only the screens you care about stay visible.

## Features

- Real multi-monitor layout map (same relative placement as Windows Display Settings)
- Mouse-idle blackout on selected displays (keyboard does not count)
- Pure Win32 dual half-screen black overlays (avoids Focus Assist fullscreen detection)
- Click-through overlays, system tray, optional start with Windows

## Requirements

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [NSIS](https://nsis.sourceforge.io/Download) (for installer builds)

```powershell
winget install --id Microsoft.DotNet.SDK.8 -e --accept-package-agreements --accept-source-agreements
winget install --id NSIS.NSIS -e --accept-package-agreements --accept-source-agreements
```

## Build installer

From the repo root:

```powershell
.\build.ps1
.\build.ps1 -Version 1.2.3
```

This publishes a self-contained `win-x64` app and builds:

`dist\MovieOverlay-Setup-<version>.exe`

Version defaults to the [`VERSION`](VERSION) file.

Dev run without installer:

```powershell
dotnet run --project src\MovieOverlay\MovieOverlay.csproj
```

## Release automation

Push to `main` runs [`.github/workflows/release.yml`](.github/workflows/release.yml):

1. Computes the next semver (first release uses [`VERSION`](VERSION); later pushes patch-bump the latest `v*` tag, or jump if you raise `VERSION` higher)
2. Runs `build.ps1`
3. Creates tag `vX.Y.Z` and a GitHub Release with the NSIS setup EXE

## Usage

1. Launch the app (settings window + tray icon).
2. Click monitors on the map to mark them for blackout (`BLACK` = overlay target, `KEEP` = stays visible).
3. Set idle seconds, save, then leave the mouse still.
4. Move the mouse to clear overlays. Tray: toggle blackout / exit.

Settings file: `%AppData%\MovieOverlay\settings.json`
