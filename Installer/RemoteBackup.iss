#define AppName "RemoteBackup Service"
#define AppPublisher "Taskscape Ltd"
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
DefaultDirName={autopf64}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
OutputDir=Output
OutputBaseFilename=RemoteBackupSetup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
CloseApplications=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
polish.OpenConfigFile=Otwórz plik konfiguracyjny (appsettings.json)
english.OpenConfigFile=Open configuration file (appsettings.json)

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "appsettings.json, appsettings.Development.json"
Source: "..\publish\appsettings.json"; DestDir: "{app}"; Flags: ignoreversion onlyifdoesntexist; Permissions: users-modify

[Dirs]
Name: "{app}\logs"; Permissions: users-modify

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"

[Run]
; Rejestracja usługi z poprawnym maskowaniem cudzysłowów dla ścieżek ze spacjami
; binPath= musi zawierać ścieżkę wewnątrz cudzysłowów, jeśli są w niej spacje
Filename: "{sys}\sc.exe"; Parameters: "create {#ServiceName} start= auto binPath= \"\"\"{app}\{#AppExeName}\"\"\" DisplayName= \"{#AppName}\""; Flags: runhidden waituntilterminated
; Opis usługi
Filename: "{sys}\sc.exe"; Parameters: "description {#ServiceName} ""Automated Remote Backup Service (FTP/HTTP)"""; Flags: runhidden waituntilterminated
; Próba uruchomienia
Filename: "{sys}\sc.exe"; Parameters: "start {#ServiceName}"; Flags: runhidden nowait
; Opcja konfiguracji
Filename: "{app}\appsettings.json"; Description: "{cm:OpenConfigFile}"; Flags: postinstall shellexec skipifsilent

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop {#ServiceName}"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "delete {#ServiceName}"; Flags: runhidden

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    // Brutalne zatrzymanie i usunięcie starej usługi przed kopiowaniem plików
    Exec('sc.exe', 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('sc.exe', 'delete {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // Czekamy chwilę, aż system Windows faktycznie usunie usługę z bazy
    Sleep(2000);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    Exec('sc.exe', 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('sc.exe', 'delete {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
