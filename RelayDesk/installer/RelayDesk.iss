#ifndef AppVersion
 #define AppVersion "0.25.5"
#endif
#ifndef PublishDir
 #define PublishDir "..\release\0.25.5"
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
PrivilegesRequired=admin
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
SetupMutex=RelayDesk.Setup
#ifndef TestBuild
AppMutex=Local\RelayDesk.Desktop
#endif

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Ярлыки:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
#ifndef TestBuild
Name: "{group}\RelayDesk"; Filename: "{app}\RelayDesk.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\RelayDesk"; Filename: "{app}\RelayDesk.exe"; WorkingDir: "{app}"; Tasks: desktopicon
#endif

[Run]
#ifndef TestBuild
Filename: "{app}\RelayDesk.exe"; Description: "Запустить RelayDesk"; Flags: nowait postinstall skipifsilent runasoriginaluser
#endif

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Remove-Startup.ps1"" -Executable ""{app}\RelayDesk.exe"""; Flags: runhidden; RunOnceId: "RemoveRelayDeskStartup"

[Messages]
russian.WelcomeLabel2=Установщик разместит RelayDesk и ядра sing-box, Xray, mihomo и Flowseal/zapret в Program Files.%n%nЯдра включены в пакет и не требуют отдельной загрузки. При первом запуске импортируй профиль или подписку. Существующие профили сохраняются в данных пользователя.
