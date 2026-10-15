#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif
#ifndef PublishDir
  #error PublishDir must point to the self-contained Windows Agent publish directory
#endif
#ifndef ReleaseDir
  #error ReleaseDir must point to a dedicated release output directory
#endif

[Setup]
AppId={{C7AA87F8-56E2-4EF2-86A9-8C1C42494445}
AppName=CYD-DECK
AppVersion={#AppVersion}
AppPublisher=Blankeuuu
AppPublisherURL=https://github.com/Blankeuuu/stream-deck
AppSupportURL=https://github.com/Blankeuuu/stream-deck/issues
AppUpdatesURL=https://github.com/Blankeuuu/stream-deck/releases
DefaultDirName={localappdata}\Programs\CYD-DECK
DefaultGroupName=CYD-DECK
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#ReleaseDir}
OutputBaseFilename=CYD-DECK-Setup-{#AppVersion}-win-x64
SetupIconFile=..\agent\assets\cyd-deck.ico
UninstallDisplayIcon={app}\StreamDeck.WindowsAgent.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
DisableProgramGroupPage=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{group}\CYD-DECK"; Filename: "{app}\StreamDeck.WindowsAgent.exe"
Name: "{autodesktop}\CYD-DECK"; Filename: "{app}\StreamDeck.WindowsAgent.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\StreamDeck.WindowsAgent.exe"; Description: "{cm:LaunchProgram,CYD-DECK}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  StartupCommand: String;
begin
  if CurUninstallStep = usUninstall then
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'StreamDeckWindowsAgent', StartupCommand) then
      if Pos(Lowercase(ExpandConstant('{app}\StreamDeck.WindowsAgent.exe')), Lowercase(StartupCommand)) > 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'StreamDeckWindowsAgent');
  { User configuration, pairing and cover caches are intentionally retained. }
end;
