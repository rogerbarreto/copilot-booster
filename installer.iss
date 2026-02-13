[Setup]
AppId={{B7E3A9D1-4F2C-4E8B-9A1D-6C5F0E2D8B47}
AppName=Copilot App
AppVersion=0.4.0
AppPublisher=Community
AppPublisherURL=https://github.com/community/copilot-app
DefaultDirName={userappdata}\CopilotApp
DefaultGroupName=Copilot App
DisableProgramGroupPage=yes
OutputDir=installer-output
OutputBaseFilename=CopilotApp-Setup
SetupIconFile=src\copilot.ico
UninstallDisplayIcon={app}\CopilotApp.exe
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=yes

[Files]
Source: "publish\CopilotApp.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish\CopilotApp.pdb"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Copilot App"; Filename: "{app}\CopilotApp.exe"; IconFilename: "{app}\CopilotApp.exe"
Name: "{userdesktop}\Copilot App"; Filename: "{app}\CopilotApp.exe"; IconFilename: "{app}\CopilotApp.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Run]
Filename: "{app}\CopilotApp.exe"; Description: "Launch Copilot App"; Flags: nowait postinstall skipifsilent
