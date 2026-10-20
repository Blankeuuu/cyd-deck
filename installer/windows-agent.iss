#ifndef AppVersion
  #define AppVersion "1.4.0"
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
AppPublisherURL=https://github.com/Blankeuuu/cyd-deck
AppSupportURL=https://github.com/Blankeuuu/cyd-deck/issues
AppUpdatesURL=https://github.com/Blankeuuu/cyd-deck/releases
DefaultDirName={localappdata}\Programs\CYD-DECK
DefaultGroupName=CYD-DECK
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#ReleaseDir}
OutputBaseFilename=CYD-DECK-Setup-{#AppVersion}-win-x64
SetupIconFile=..\agent\assets\cyd-deck.ico
UninstallDisplayIcon={app}\CYD-DECK.exe
UninstallDisplayName=CYD-DECK
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

[InstallDelete]
Type: files; Name: "{app}\StreamDeck.WindowsAgent.exe"
Type: files; Name: "{app}\StreamDeck.WindowsAgent.deps.json"
Type: files; Name: "{app}\StreamDeck.WindowsAgent.runtimeconfig.json"
Type: files; Name: "{group}\StreamDeck.WindowsAgent.lnk"
Type: files; Name: "{autodesktop}\StreamDeck.WindowsAgent.lnk"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{group}\CYD-DECK"; Filename: "{app}\CYD-DECK.exe"; AppUserModelID: "Blankeuuu.CYD-DECK"
Name: "{autodesktop}\CYD-DECK"; Filename: "{app}\CYD-DECK.exe"; Tasks: desktopicon; AppUserModelID: "Blankeuuu.CYD-DECK"

[Run]
Filename: "{app}\CYD-DECK.exe"; Description: "{cm:LaunchProgram,CYD-DECK}"; Flags: nowait postinstall

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
end;

function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  Exec('taskkill.exe', '/F /IM CYD-DECK.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/F /IM StreamDeck.WindowsAgent.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function TryExtractCommandArgsForExe(const CommandLine, ExpectedExePath: String; var ArgsSuffix: String): Boolean;
var
  Trimmed: String;
  QuotedPrefix: String;
  PrefixLen: Integer;
begin
  Result := False;
  ArgsSuffix := '';
  Trimmed := Trim(CommandLine);
  if Trimmed = '' then
    Exit;

  QuotedPrefix := '"' + ExpectedExePath + '"';
  PrefixLen := Length(QuotedPrefix);
  if Copy(Lowercase(Trimmed), 1, PrefixLen) = Lowercase(QuotedPrefix) then
  begin
    if (Length(Trimmed) = PrefixLen) or (Trimmed[PrefixLen + 1] = ' ') then
    begin
      ArgsSuffix := Trim(Copy(Trimmed, PrefixLen + 1, Length(Trimmed) - PrefixLen));
      Result := True;
      Exit;
    end;
  end;

  PrefixLen := Length(ExpectedExePath);
  if Copy(Lowercase(Trimmed), 1, PrefixLen) = Lowercase(ExpectedExePath) then
  begin
    if (Length(Trimmed) = PrefixLen) or (Trimmed[PrefixLen + 1] = ' ') then
    begin
      ArgsSuffix := Trim(Copy(Trimmed, PrefixLen + 1, Length(Trimmed) - PrefixLen));
      Result := True;
      Exit;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  LegacyStartupCommand: String;
  ExistingCurrentCommand: String;
  ArgsSuffix: String;
  NewStartupCommand: String;
begin
  if CurStep = ssPostInstall then
  begin
    { If legacy autostart entry exists and starts with our app executable, migrate it while preserving arguments and without overwriting an existing CYD-DECK entry }
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'StreamDeckWindowsAgent', LegacyStartupCommand) then
    begin
      if TryExtractCommandArgsForExe(LegacyStartupCommand, ExpandConstant('{app}\StreamDeck.WindowsAgent.exe'), ArgsSuffix) or
         TryExtractCommandArgsForExe(LegacyStartupCommand, ExpandConstant('{app}\CYD-DECK.exe'), ArgsSuffix) then
      begin
        if (not RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'CYD-DECK', ExistingCurrentCommand)) or
           (Trim(ExistingCurrentCommand) = '') then
        begin
          NewStartupCommand := '"' + ExpandConstant('{app}\CYD-DECK.exe') + '"';
          if ArgsSuffix <> '' then
            NewStartupCommand := NewStartupCommand + ' ' + ArgsSuffix;
          RegWriteStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'CYD-DECK', NewStartupCommand);
        end;
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'StreamDeckWindowsAgent');
      end;
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  StartupCommand: String;
  UnusedArgs: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'CYD-DECK', StartupCommand) then
      if TryExtractCommandArgsForExe(StartupCommand, ExpandConstant('{app}\CYD-DECK.exe'), UnusedArgs) or
         TryExtractCommandArgsForExe(StartupCommand, ExpandConstant('{app}\StreamDeck.WindowsAgent.exe'), UnusedArgs) then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'CYD-DECK');

    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'StreamDeckWindowsAgent', StartupCommand) then
      if TryExtractCommandArgsForExe(StartupCommand, ExpandConstant('{app}\CYD-DECK.exe'), UnusedArgs) or
         TryExtractCommandArgsForExe(StartupCommand, ExpandConstant('{app}\StreamDeck.WindowsAgent.exe'), UnusedArgs) then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'StreamDeckWindowsAgent');
  end;
  { User configuration, pairing and cover caches are intentionally retained. }
end;
