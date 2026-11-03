; SanitaryPOS.nsi — NSIS installer for the Bilingual Sanitary Store POS.
;
; Builds SanitaryPOS-Setup.exe, which installs the self-contained
; SanitaryPOS.exe (no .NET installation needed) for the CURRENT Windows
; user — no administrator password required:
;   Program : %LocalAppData%\Programs\SanitaryPOS\SanitaryPOS.exe
;   Data    : %AppData%\SanitaryPOS\sanitarypos.db  (NOT installed here and
;             NEVER touched by install / upgrade / uninstall — the shop's
;             data survives reinstalling the app.)
;
; Build (any OS with NSIS 3.x):
;   1. Publish the app exe first:
;        dotnet publish src/SanitaryPOS -c Release -r win-x64 --self-contained true ^
;          -p:EnableWindowsTargeting=true -p:PublishSingleFile=true ^
;          -p:IncludeNativeLibrariesForSelfExtract=true -o publish
;   2. makensis installer/SanitaryPOS.nsi
;      (expects ..\publish\SanitaryPOS.exe relative to this script)

Unicode True
RequestExecutionLevel user

!include "MUI2.nsh"

Name "Sanitary POS"
OutFile "SanitaryPOS-Setup.exe"
InstallDir "$LOCALAPPDATA\Programs\SanitaryPOS"
InstallDirRegKey HKCU "Software\SanitaryPOS" "InstallDir"

VIProductVersion "1.0.0.0"
VIAddVersionKey "ProductName" "Sanitary POS"
VIAddVersionKey "FileDescription" "Sanitary POS Setup (Bilingual Urdu/English)"
VIAddVersionKey "FileVersion" "1.0.0.0"
VIAddVersionKey "CompanyName" "Sanitary POS"

!define MUI_FINISHPAGE_RUN "$INSTDIR\SanitaryPOS.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Run Sanitary POS now"
!define MUI_WELCOMEPAGE_TEXT "This will install Sanitary POS (Bilingual Urdu / English) on your computer.$\r$\n$\r$\nYour shop data is stored separately in your Windows user folder (AppData\SanitaryPOS), so installing, upgrading or uninstalling this app will NOT delete your products, bills or khata."

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Section "Install"
  SetOutPath "$INSTDIR"
  ; The single self-contained application exe (replaced on upgrade).
  File "..\publish\SanitaryPOS.exe"

  WriteRegStr HKCU "Software\SanitaryPOS" "InstallDir" "$INSTDIR"

  ; Start Menu shortcuts
  CreateDirectory "$SMPROGRAMS\Sanitary POS"
  CreateShortcut "$SMPROGRAMS\Sanitary POS\Sanitary POS.lnk" "$INSTDIR\SanitaryPOS.exe"
  CreateShortcut "$SMPROGRAMS\Sanitary POS\Uninstall.lnk" "$INSTDIR\Uninstall.exe"

  ; Desktop shortcut
  CreateShortcut "$DESKTOP\Sanitary POS.lnk" "$INSTDIR\SanitaryPOS.exe"

  ; Uninstaller + Add/Remove Programs entry (current user)
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SanitaryPOS" "DisplayName" "Sanitary POS"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SanitaryPOS" "DisplayVersion" "1.0.0"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SanitaryPOS" "DisplayIcon" "$INSTDIR\SanitaryPOS.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SanitaryPOS" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SanitaryPOS" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SanitaryPOS" "NoRepair" 1
SectionEnd

Section "Uninstall"
  Delete "$INSTDIR\SanitaryPOS.exe"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"

  Delete "$SMPROGRAMS\Sanitary POS\Sanitary POS.lnk"
  Delete "$SMPROGRAMS\Sanitary POS\Uninstall.lnk"
  RMDir "$SMPROGRAMS\Sanitary POS"
  Delete "$DESKTOP\Sanitary POS.lnk"

  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SanitaryPOS"
  DeleteRegKey HKCU "Software\SanitaryPOS"

  ; NOTE: %AppData%\SanitaryPOS (the shop database + backups) is deliberately
  ; left in place — uninstalling the app must never delete shop data.
SectionEnd
