; AURA Native installer (Inno Setup 6). Built by the GitHub release workflow.
;   iscc /DAppVersion=1.2.0 installer.iss   ->  dist\AURA-Native-Setup.exe
;
; Installs for the current user only (no admin prompt). AURA Native keeps its
; library in %APPDATA%\AURA Native and is a separate app from the first AURA:
; both can be installed side by side.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
; Keep this AppId the same forever (it is how Windows knows a new installer is an update).
AppId={{B3D2E5A1-7C44-4F0B-9E61-5A8C2D7F1E39}
AppName=AURA Native
AppVersion={#AppVersion}
AppVerName=AURA Native {#AppVersion}
AppPublisher=Evan
DefaultDirName={localappdata}\Programs\AURA Native
DefaultGroupName=AURA Native
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest

OutputDir=dist
OutputBaseFilename=AURA-Native-Setup
SetupIconFile=icon.ico
UninstallDisplayIcon={app}\AURA Native.exe
UninstallDisplayName=AURA Native
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "bin\Release\AURA Native.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\Release\AURA Native.exe.config"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\AURA Native"; Filename: "{app}\AURA Native.exe"
Name: "{autodesktop}\AURA Native"; Filename: "{app}\AURA Native.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\AURA Native.exe"; Description: "Open AURA Native"; Flags: nowait postinstall skipifsilent
; after a self-update (a silent install), open AURA again
Filename: "{app}\AURA Native.exe"; Flags: nowait; Check: WizardSilent
