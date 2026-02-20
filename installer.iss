[Setup]
AppId={{D4A8F1E2-7B3C-4D9E-A6C2-8E1F5B3D9A72}
AppName=Copilot Booster
AppVersion=0.13.2
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
Source: "publish\session.html"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish\copilot.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Copilot Booster"; Filename: "{app}\CopilotBooster.exe"; IconFilename: "{app}\CopilotBooster.exe"; AppUserModelID: "CopilotBooster"
Name: "{userdesktop}\Copilot Booster"; Filename: "{app}\CopilotBooster.exe"; IconFilename: "{app}\CopilotBooster.exe"; Tasks: desktopicon; AppUserModelID: "CopilotBooster"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Run]
Filename: "{app}\CopilotBooster.exe"; Description: "Launch Copilot Booster"; Flags: nowait postinstall skipifsilent
