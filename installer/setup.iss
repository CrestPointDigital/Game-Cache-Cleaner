; Inno Setup script to create a Windows installer with a wizard
; Requires Inno Setup (https://jrsoftware.org/isinfo.php)

[Setup]
AppId={{50C5E205-6A3E-4A07-A0C0-89D0BB7D2C9E}
AppName=Game Cache Cleaner
AppVersion=1.0.0
AppPublisher=CrestPoint Digital
DefaultDirName={pf}\CrestPoint Digital\Game Cache Cleaner
DefaultGroupName=Game Cache Cleaner
DisableDirPage=no
DisableProgramGroupPage=no
SetupIconFile=..\GameCacheCleaner.UI\Assets\crestpoint.ico
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
OutputDir=..
OutputBaseFilename=GameCacheCleaner_Setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
; Use the published single-file exe if available; otherwise use Release build exe
Source: "..\\GameCacheCleaner_UI_Release\\GameCacheCleaner.UI.exe"; DestDir: "{app}"; Flags: ignoreversion
; Include the icon so tray/window can load it from Assets
Source: "..\\GameCacheCleaner.UI\\Assets\\crestpoint.ico"; DestDir: "{app}\\Assets"; Flags: ignoreversion

[Icons]
Name: "{group}\Game Cache Cleaner"; Filename: "{app}\\GameCacheCleaner.UI.exe"; IconFilename: "{app}\\Assets\\crestpoint.ico"
Name: "{userdesktop}\Game Cache Cleaner"; Filename: "{app}\\GameCacheCleaner.UI.exe"; Tasks: desktopicon; IconFilename: "{app}\\Assets\\crestpoint.ico"

[Run]
Filename: "{app}\\GameCacheCleaner.UI.exe"; Description: "Launch Game Cache Cleaner"; Flags: nowait postinstall skipifsilent
