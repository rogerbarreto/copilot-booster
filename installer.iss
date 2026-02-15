[Setup]
AppId={{B7E3A9D1-4F2C-4E8B-9A1D-6C5F0E2D8B47}
AppName=Copilot Booster
AppVersion=0.9.0
AppPublisher=Community
AppPublisherURL=https://github.com/rogerbarreto/copilot-booster
DefaultDirName={userappdata}\CopilotBooster
DefaultGroupName=Copilot Booster
DisableProgramGroupPage=yes
OutputDir=installer-output
OutputBaseFilename=CopilotBooster-Setup
SetupIconFile=src\copilot.ico
UninstallDisplayIcon={app}\CopilotBooster.exe
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=yes

[Files]
Source: "publish\CopilotBooster.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish\CopilotBooster.pdb"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Copilot Booster"; Filename: "{app}\CopilotBooster.exe"; IconFilename: "{app}\CopilotBooster.exe"
Name: "{userdesktop}\Copilot Booster"; Filename: "{app}\CopilotBooster.exe"; IconFilename: "{app}\CopilotBooster.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Run]
Filename: "{app}\CopilotBooster.exe"; Description: "Launch Copilot Booster"; Flags: nowait postinstall skipifsilent
