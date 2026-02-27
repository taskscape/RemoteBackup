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
; Use 64-bit Program Files directory
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
; Required for 64-bit installation
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
polish.OpenConfigFile=Otwórz plik konfiguracyjny (appsettings.json)
english.OpenConfigFile=Open configuration file (appsettings.json)

[Files]
; Exclude appsettings.Development.json and the main appsettings.json from the bulk copy
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "appsettings.json, appsettings.Development.json"
; Handle appsettings.json separately:
; 1. onlyifdoesntexist: to keep your settings during updates
; 2. Permissions: users-modify: allows you to save changes right after install without running as admin
Source: "..\publish\appsettings.json"; DestDir: "{app}"; Flags: ignoreversion onlyifdoesntexist; Permissions: users-modify

[Dirs]
; Create logs directory and grant modify permissions
Name: "{app}\logs"; Permissions: users-modify

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"

[Run]
; Install the service using sc.exe
Filename: "{sys}\sc.exe"; Parameters: "create {#ServiceName} start= auto binPath= ""{app}\{#AppExeName}"" DisplayName= ""{#AppName}"""; Flags: runhidden
; Set description
Filename: "{sys}\sc.exe"; Parameters: "description {#ServiceName} ""Automated Remote Backup Service (FTP/HTTP)"""; Flags: runhidden
; Start the service
Filename: "{sys}\sc.exe"; Parameters: "start {#ServiceName}"; Flags: runhidden
; Option to open config file
Filename: "{app}\appsettings.json"; Description: "{cm:OpenConfigFile}"; Flags: postinstall shellexec skipifsilent

[UninstallRun]
; Stop and delete the service
Filename: "{sys}\sc.exe"; Parameters: "stop {#ServiceName}"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "delete {#ServiceName}"; Flags: runhidden

[Code]
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  Exec('sc.exe', 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
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
