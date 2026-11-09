#ifndef AppVersion
 #define AppVersion "0.30.0"
#endif
#ifndef PublishDir
 #define PublishDir "..\release\0.30.0"
#endif
#ifdef TestBuild
 #define ProductName "RelayDesk Installer QA"
 #define ProductId "RelayDesk-Installer-QA-4F386E9C"
 #define SetupName "RelayDesk-Setup-QA"
#else
 #define ProductName "RelayDesk"
 #define ProductId "RelayDesk-Desktop-A7B02E29-4D16-47F0-B8F3-3B829E1F024D"
 #define SetupName "RelayDesk-Setup-" + AppVersion + "-x64"
#endif

[Setup]
AppId={#ProductId}
AppName={#ProductName}
AppVersion={#AppVersion}
AppPublisher=RelayDesk
VersionInfoDescription=Установка RelayDesk
DefaultDirName={autopf}\{#ProductName}
DefaultGroupName={#ProductName}
DisableProgramGroupPage=yes
#ifdef HandoffTestBuild
PrivilegesRequired=lowest
#else
PrivilegesRequired=admin
#endif
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=output
OutputBaseFilename={#SetupName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\Assets\RelayDesk.ico
UninstallDisplayIcon={app}\RelayDesk.exe
UninstallDisplayName={#ProductName}
CloseApplications=no
RestartApplications=no
#ifdef TestBuild
SetupMutex=RelayDesk.Setup.QA
#else
SetupMutex=RelayDesk.Setup
#endif

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Ярлыки:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,Prepare-Install.ps1"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PublishDir}\Prepare-Install.ps1"; Flags: dontcopy

[Icons]
#ifndef TestBuild
Name: "{group}\RelayDesk"; Filename: "{app}\RelayDesk.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\RelayDesk"; Filename: "{app}\RelayDesk.exe"; WorkingDir: "{app}"; Tasks: desktopicon
#endif

[Run]
#ifndef TestBuild
Filename: "{app}\RelayDesk.exe"; Parameters: "--resume-update {code:GetResumeToken}"; Flags: nowait; Check: ShouldResume; AfterInstall: MarkResumed
Filename: "{app}\RelayDesk.exe"; Description: "Запустить RelayDesk"; Flags: nowait postinstall skipifsilent runasoriginaluser; Check: NormalLaunch
#endif

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Remove-Startup.ps1"" -Executable ""{app}\RelayDesk.exe"""; Flags: runhidden; RunOnceId: "RemoveRelayDeskStartup"

[Messages]
russian.WelcomeLabel2=Установщик разместит RelayDesk и ядра sing-box, Xray, mihomo и Flowseal/zapret в Program Files.%n%nЯдра включены в пакет и не требуют отдельной загрузки. При первом запуске импортируй профиль или подписку. Существующие профили сохраняются в данных пользователя.

[Code]
var
  ResumeToken: String;
  Prepared: Boolean;
  Resumed: Boolean;

function GetResumeToken(Param: String): String;
begin
  Result := ResumeToken;
end;

function ShouldResume: Boolean;
begin
  Result := (ResumeToken <> '') and (ExpandConstant('{param:NOLAUNCH|0}') <> '1');
end;

function NormalLaunch: Boolean;
begin
  Result := (ResumeToken = '') and (ExpandConstant('{param:NOLAUNCH|0}') <> '1');
end;

procedure MarkResumed;
begin
  Resumed := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
  Args: String;
  TokenText: AnsiString;
begin
  Result := '';
  if Prepared then Exit;
#ifdef TestBuild
#ifndef HandoffTestBuild
  Prepared := True;
  Exit;
#endif
#endif
  ExtractTemporaryFile('Prepare-Install.ps1');
  Args := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\Prepare-Install.ps1') + '" -ResultFile "' + ExpandConstant('{tmp}\resume-token.txt') + '"';
  ResumeToken := ExpandConstant('{param:RESUMEUPDATE|}');
#ifdef HandoffTestBuild
  Args := Args + ' -OnlyProcessId ' + ExpandConstant('{param:QAPID|0}') + ' -DataFolder "' + ExpandConstant('{param:QADATA|}') + '"';
  if ExpandConstant('{param:QAPID|0}') = '0' then
  begin
    Result := 'Missing isolated QA process';
    Exit;
  end;
#endif
  if ResumeToken <> '' then Args := Args + ' -ResumeToken "' + ResumeToken + '"';
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Args, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    Result := 'Не удалось подготовить RelayDesk к обновлению.'
  else if ExitCode <> 0 then
    Result := 'RelayDesk не завершил подготовку. Сохрани текущую операцию и повтори установку.'
  else if LoadStringFromFile(ExpandConstant('{tmp}\resume-token.txt'), TokenText) then
  begin
    ResumeToken := String(TokenText);
    Prepared := True;
  end
  else Result := 'Не удалось прочитать состояние перезапуска.';
end;

procedure DeinitializeSetup;
var
  ExitCode: Integer;
begin
  if Prepared and (not Resumed) and ShouldResume and FileExists(ExpandConstant('{app}\RelayDesk.exe')) then
    Exec(ExpandConstant('{app}\RelayDesk.exe'), '--resume-update ' + ResumeToken, ExpandConstant('{app}'), SW_SHOWNORMAL, ewNoWait, ExitCode);
end;
