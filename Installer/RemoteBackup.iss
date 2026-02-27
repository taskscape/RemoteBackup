#define AppName "RemoteBackup Service"
#define AppPublisher "Taskscape Ltd"
#define AppExeName "BackupService.exe"
#define ServiceName "BackupService"

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
; sc.exe wymaga spacji po '='
Filename: "{sys}\sc.exe"; Parameters: "create {#ServiceName} start= auto binPath= ""{app}\{#AppExeName}"" DisplayName= ""{#AppName}"""; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "description {#ServiceName} ""Automated Remote Backup Service (FTP/HTTP)"""; Flags: runhidden waituntilterminated
; Zmiana na waituntilterminated dla pewności startu
Filename: "{sys}\sc.exe"; Parameters: "start {#ServiceName}"; Flags: runhidden waituntilterminated
Filename: "{app}\appsettings.json"; Description: "{cm:OpenConfigFile}"; Flags: postinstall shellexec skipifsilent

[UninstallRun]
; Używamy pełnej ścieżki systemowej do sc.exe
Filename: "{sys}\sc.exe"; Parameters: "stop {#ServiceName}"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "delete {#ServiceName}"; Flags: runhidden

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  ScPath: string;
begin
  if CurStep = ssInstall then
  begin
    // Pobieramy pełną ścieżkę do sc.exe
    ScPath := ExpandConstant('{sys}\sc.exe');

    // Wymuszamy zatrzymanie i usunięcie starej usługi przed kopiowaniem plików
    Exec(ScPath, 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(ScPath, 'delete {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    
    // Czekamy na zwolnienie blokad plików
    Sleep(2000);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
  ScPath: string;
begin
  if CurUninstallStep = usUninstall then
  begin
    ScPath := ExpandConstant('{sys}\sc.exe');
    Exec(ScPath, 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(ScPath, 'delete {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
