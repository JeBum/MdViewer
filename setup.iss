#define MyAppName "MDviewer"
#define MyAppVersion "1.3.0"
#define MyAppPublisher "Sogang MOT"
#define MyAppExeName "MDviewer.exe"

[Setup]
AppId={{8C3E1A6B-4D2F-4E9A-9C11-7B2F0E6A4D91}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\MDviewer
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=installer
OutputBaseFilename=MDviewerSetup
SetupIconFile=assets\app.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
Name: "assoc"; Description: ".md file association"; GroupDescription: "Association:"; Flags: checkedonce

[Files]
Source: "publish_install\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Classes\.md"; ValueType: string; ValueName: ""; ValueData: "MDviewer.markdown"; Flags: uninsdeletevalue; Tasks: assoc
Root: HKCU; Subkey: "Software\Classes\.md\OpenWithProgids"; ValueType: string; ValueName: "MDviewer.markdown"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assoc
Root: HKCU; Subkey: "Software\Classes\.markdown"; ValueType: string; ValueName: ""; ValueData: "MDviewer.markdown"; Flags: uninsdeletevalue; Tasks: assoc
Root: HKCU; Subkey: "Software\Classes\MDviewer.markdown"; ValueType: string; ValueName: ""; ValueData: "Markdown Document"; Flags: uninsdeletekey; Tasks: assoc
Root: HKCU; Subkey: "Software\Classes\MDviewer.markdown\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: assoc
Root: HKCU; Subkey: "Software\Classes\MDviewer.markdown\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: assoc
Root: HKCU; Subkey: "Software\MDviewer\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "MDviewer"; Tasks: assoc
Root: HKCU; Subkey: "Software\MDviewer\Capabilities\FileAssociations"; ValueType: string; ValueName: ".md"; ValueData: "MDviewer.markdown"; Tasks: assoc
Root: HKCU; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "MDviewer"; ValueData: "Software\MDviewer\Capabilities"; Flags: uninsdeletevalue; Tasks: assoc

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch MDviewer"; Flags: nowait postinstall skipifsilent
