; BetterPrompt Installer — built by GitHub Actions and by Inno Setup locally
; Run: ISCC.exe /DAppVersion=1.0.0 BetterPrompt.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppName=BetterPrompt
AppVersion={#AppVersion}
AppVerName=BetterPrompt {#AppVersion}
AppPublisher=Dan Clark
AppPublisherURL=https://github.com/horkam/BetterPrompt
AppSupportURL=https://github.com/horkam/BetterPrompt/issues
AppUpdatesURL=https://github.com/horkam/BetterPrompt/releases

DefaultDirName={autopf}\BetterPrompt
DefaultGroupName=BetterPrompt
DisableProgramGroupPage=yes

OutputDir=Output
OutputBaseFilename=BetterPrompt-Setup

SetupIconFile=..\BetterPrompt\Assets\app.ico
UninstallDisplayIcon={app}\BetterPrompt.exe
UninstallDisplayName=BetterPrompt

Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=100

; Require Windows 10+ (matches .NET 8 WPF minimum)
MinVersion=10.0

; 64-bit only — we publish win-x64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\BetterPrompt.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\BetterPrompt";                          Filename: "{app}\BetterPrompt.exe"
Name: "{group}\{cm:UninstallProgram,BetterPrompt}";    Filename: "{uninstallexe}"
Name: "{autodesktop}\BetterPrompt";                    Filename: "{app}\BetterPrompt.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\BetterPrompt.exe"; \
  Description: "{cm:LaunchProgram,BetterPrompt}"; \
  Flags: nowait postinstall skipifsilent
