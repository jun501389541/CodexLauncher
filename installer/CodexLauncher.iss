#define AppName "Codex 启动器"
#define AppVersion "1.0.4"
#define AppExeName "CodexLauncher.exe"
#define PublishDir "..\dist\CodexLauncher-v1.0.4-win-x64-self-contained"

[Setup]
AppId={{72be3a1c-2cd5-44a9-86a8-6c9b71636b14}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=CodexLauncher
DefaultDirName={localappdata}\Programs\CodexLauncher
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=CodexLauncher-v1.0.4-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
Uninstallable=yes
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\CodexLauncher.exe
VersionInfoVersion=1.0.4.0
VersionInfoDescription=CodexLauncher Windows installer
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}

[Languages]
Name: "chinesesimplified"; MessagesFile: "languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加快捷方式："; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "CodexLauncher.installed"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "CodexLauncher"; Flags: dontcreatekey uninsdeletevalue

[UninstallDelete]
Type: filesandordirs; Name: "{app}\portable-data"
Type: filesandordirs; Name: "{localappdata}\CodexLauncher"

[Messages]
ConfirmUninstall=是否卸载 Codex 启动器？卸载会永久删除程序目录中的设置、日志和桥接数据，以及旧版 %LOCALAPPDATA%\CodexLauncher 数据。
