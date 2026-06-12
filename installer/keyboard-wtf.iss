#define MyAppName "keyboard.wtf"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "keyboard.wtf"
#define MyAppExeName "keyboard.wtf.exe"

[Setup]
AppId={{A2290D45-0672-4FC5-8CFB-AEB92B1EF945}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\keyboard.wtf
DefaultGroupName=keyboard.wtf
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\dist\installer
OutputBaseFilename=keyboard-wtf-setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=force
RestartApplications=no
SetupLogging=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoDescription=keyboard.wtf Windows installer
VersionInfoCompany={#MyAppPublisher}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\dist\release\keyboard-wtf-win-x64\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\keyboard.wtf"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "keyboard.wtf"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch keyboard.wtf and finish setup"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
