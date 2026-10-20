#ifndef AppVersion
  #define AppVersion "0.0.0-local"
#endif

#ifndef PublishDir
  #define PublishDir "..\obj\installer-publish"
#endif

#define AppName "Codex + Claude Usage Widget"
#define AppExeName "CodexClaudeUsageWidget.exe"

[Setup]
AppId={{C41F2A8D-B611-4A7C-A3FD-4608A901562D}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=jepicode
DefaultDirName={localappdata}\Programs\CodexClaudeUsageWidget
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
OutputBaseFilename=CodexClaudeUsageWidget-Setup
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
DisableProgramGroupPage=yes
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional options:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: postinstall nowait skipifsilent
