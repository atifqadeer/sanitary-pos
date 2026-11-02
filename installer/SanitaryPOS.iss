; ============================================================
;  Sanitary POS — Inno Setup 6 installer script (Phase 3)
;  Build steps: see BUILD-INSTALLER.md in this folder.
;
;  What this installer does:
;   - Installs the published app files from a `publish\` folder
;     (set PublishDir below if you keep them somewhere else).
;   - Creates Start Menu + Desktop shortcuts.
;   - Optionally installs the Noto Nastaliq Urdu font (needed for
;     proper Urdu text) when a `fonts\` folder sits next to this
;     script — see BUILD-INSTALLER.md for where to get the font.
;   - CHECKS for the .NET 8 Desktop Runtime and stops with a clear
;     message + download link if it is missing. The runtime itself
;     is NOT bundled (keeps this setup small; the shop PC downloads
;     it once from Microsoft).
;   - Full uninstall support (Programs and Features entry).
;  The app's data (database + backups) lives in %AppData%\SanitaryPOS
;  and is deliberately NOT removed by uninstall, so reinstalling or
;  upgrading never wipes the shop's records.
; ============================================================

#define AppName "Sanitary POS"
#define AppVersion "1.0.0"
#define AppPublisher "Your Shop Name"
#define AppExeName "SanitaryPOS.exe"
; Folder (relative to this script) containing the `dotnet publish` output:
#define PublishDir "publish"
; Optional folder with Noto Nastaliq Urdu font files (.ttf). If the folder
; or file is absent at compile time, just delete/ignore the [Files] font
; line below before compiling.
#define FontDir "fonts"

[Setup]
AppId={{8F3A2B1C-4D5E-4F6A-9B7C-1A2B3C4D5E6F}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\SanitaryPOS
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=SanitaryPOS-Setup-1.0.0
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
; The published application (framework-dependent build — see BUILD-INSTALLER.md).
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Noto Nastaliq Urdu font for correct Urdu rendering. fontinstall registers
; it as a system font; onlyifdoesntexist leaves a newer existing copy alone.
; REMOVE this line if you did not download the font (see BUILD-INSTALLER.md).
Source: "{#FontDir}\NotoNastaliqUrdu-Regular.ttf"; DestDir: "{autofonts}"; FontInstall: "Noto Nastaliq Urdu"; Flags: onlyifdoesntexist uninsneveruninstall fontinstall

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
{ ------------------------------------------------------------------
  .NET 8 Desktop Runtime prerequisite check.
  Looks for the Microsoft.WindowsDesktop.App 8.x shared framework
  folder that the runtime installer creates. If it is not there,
  setup stops BEFORE copying anything and tells the user exactly
  where to get the free runtime from Microsoft.
  ------------------------------------------------------------------ }
function IsDotNet8DesktopInstalled(): Boolean;
var
  FindRec: TFindRec;
  BaseDir: String;
begin
  Result := False;
  BaseDir := ExpandConstant('{autopf}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if FindFirst(BaseDir + '\8.*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          Result := True;
          Break;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not IsDotNet8DesktopInstalled() then
  begin
    MsgBox('Sanitary POS needs the Microsoft .NET 8 Desktop Runtime (free), ' +
           'which is not installed on this computer.' + #13#10 + #13#10 +
           'Please install it first from Microsoft:' + #13#10 +
           'https://dotnet.microsoft.com/en-us/download/dotnet/8.0' + #13#10 +
           '(choose ".NET Desktop Runtime 8.x" for Windows x64)' + #13#10 + #13#10 +
           'Then run this setup again.',
           mbError, MB_OK);
    Result := False;
  end;
end;
