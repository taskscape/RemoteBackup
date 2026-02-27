#define AppName "RemoteBackup Service"
#define AppPublisher "RemoteBackup Team"
#define AppURL "https://github.com/skuziora/RemoteBackup"
#define AppExeName "BackupService.exe"
#define ServiceName "BackupService"

; Get version from command line or default to 1.0.0
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{D0E7B26C-B234-4A82-841F-43C3A3311E6A}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
OutputDir=Output
OutputBaseFilename=RemoteBackupSetup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
CloseApplications=yes

[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
polish.OpenConfigFile=Otwórz plik konfiguracyjny (appsettings.json)
english.OpenConfigFile=Open configuration file (appsettings.json)

[Files]
; Najpierw kopiujemy wszystko PRÓCZ pliku konfiguracyjnego
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "appsettings.json"
; Plik konfiguracyjny kopiujemy osobno - nie nadpisujemy go przy aktualizacji (zachowujemy ustawienia użytkownika)
Source: "..\publish\appsettings.json"; DestDir: "{app}"; Flags: ignoreversion onlyifdoesntexist

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"

[Run]
; Instalacja serwisu
Filename: "{sys}\sc.exe"; Parameters: "create {#ServiceName} start= auto binPath= ""{app}\{#AppExeName}"" DisplayName= ""{#AppName}"""; Flags: runhidden
; Opis serwisu
Filename: "{sys}\sc.exe"; Parameters: "description {#ServiceName} ""Automated Remote Backup Service (FTP/HTTP)"""; Flags: runhidden
; Uruchomienie serwisu
Filename: "{sys}\sc.exe"; Parameters: "start {#ServiceName}"; Flags: runhidden
; OPCJA: Otwarcie pliku konfiguracyjnego na koniec instalacji
Filename: "{app}\appsettings.json"; Description: "{cm:OpenConfigFile}"; Flags: postinstall shellexec skipifsilent

[UninstallRun]
; Zatrzymanie i usunięcie serwisu przy deinstalacji
Filename: "{sys}\sc.exe"; Parameters: "stop {#ServiceName}"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "delete {#ServiceName}"; Flags: runhidden

[Code]
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  // Próba zatrzymania serwisu przed instalacją nowej wersji
  Exec('sc.exe', 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    // Usuwamy stary serwis przed nową instalacją, aby upewnić się, że ścieżka i parametry są aktualne
    Exec('sc.exe', 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('sc.exe', 'delete {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(1000);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    Exec('sc.exe', 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(1000);
    Exec('sc.exe', 'delete {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
