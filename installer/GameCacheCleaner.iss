; === Game Cache Cleaner Installer (Inno Setup) ===
#define MyAppName "Game Cache Cleaner"
#define MyAppPublisher "CrestPoint Digital"
#define MyAppVersion "1.0.1"    ; <-- bump per release
#define MyAppExeName "GameCacheCleaner.UI.exe"
#define PublishDir "..\\GameCacheCleaner.UI\\bin\\Release\\net8.0-windows\\win-x64\\publish"
; Output filename with version

[Setup]
; keep AppId stable once chosen
AppId={{7D7E9A1F-1AF7-4F0E-980F-4B2C6A75A9E8}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\\CrestPoint Digital\\Game Cache Cleaner
DefaultGroupName={#MyAppName}
OutputBaseFilename=GameCacheCleaner_Setup_v{#MyAppVersion}
OutputDir=..\\
Compression=lzma2
SolidCompression=yes
DisableDirPage=no
DisableProgramGroupPage=yes
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64
SetupIconFile={#PublishDir}\\Assets\\crestpoint.ico
UninstallDisplayIcon={app}\\Assets\\crestpoint.ico
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PublishDir}\\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{autoprograms}\\{#MyAppName}"; Filename: "{app}\\{#MyAppExeName}"; IconFilename: "{app}\\Assets\\crestpoint.ico"
Name: "{userdesktop}\\{#MyAppName}"; Filename: "{app}\\{#MyAppExeName}"; IconFilename: "{app}\\Assets\\crestpoint.ico"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"

[Run]
Filename: "{app}\\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
