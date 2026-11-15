; Movie Overlay NSIS installer
; Build with: makensis /DAPP_VERSION=x.y.z /DPUBLISH_DIR=...\publish\app MovieOverlay.nsi

!ifndef APP_VERSION
  !define APP_VERSION "0.0.0"
!endif

!ifndef PUBLISH_DIR
  !define PUBLISH_DIR "..\publish\app"
!endif

!ifndef OUT_FILE
  !define OUT_FILE "..\publish\MovieOverlay-Setup-${APP_VERSION}.exe"
!endif

!define APP_NAME "Movie Overlay"
!define APP_EXE "MovieOverlay.exe"
!define APP_PUBLISHER "Movie Overlay"
!define APP_REG_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\MovieOverlay"

Name "${APP_NAME} ${APP_VERSION}"
OutFile "${OUT_FILE}"
InstallDir "$LOCALAPPDATA\MovieOverlay"
InstallDirRegKey HKCU "${APP_REG_KEY}" "InstallLocation"
RequestExecutionLevel user
Unicode true
SetCompressor /SOLID lzma
ShowInstDetails show
ShowUnInstDetails show

Page directory
Page instfiles
UninstPage uninstConfirm
UninstPage instfiles

Section "Install"
  SetOutPath "$INSTDIR"
  File /r "${PUBLISH_DIR}\*.*"

  CreateDirectory "$SMPROGRAMS\${APP_NAME}"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\Uninstall.lnk" "$INSTDIR\Uninstall.exe"
  CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"

  WriteUninstaller "$INSTDIR\Uninstall.exe"

  WriteRegStr HKCU "${APP_REG_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKCU "${APP_REG_KEY}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "${APP_REG_KEY}" "Publisher" "${APP_PUBLISHER}"
  WriteRegStr HKCU "${APP_REG_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${APP_REG_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE}"
  WriteRegStr HKCU "${APP_REG_KEY}" "UninstallString" "$INSTDIR\Uninstall.exe"
  WriteRegDWORD HKCU "${APP_REG_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${APP_REG_KEY}" "NoRepair" 1
SectionEnd

Section "Uninstall"
  Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}\Uninstall.lnk"
  RMDir "$SMPROGRAMS\${APP_NAME}"
  Delete "$DESKTOP\${APP_NAME}.lnk"

  DeleteRegKey HKCU "${APP_REG_KEY}"
  RMDir /r "$INSTDIR"
SectionEnd
