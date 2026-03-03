# BackupService

Windows service for scheduled FTP/FTPS backups. It supports two main FTP modes:
1. **Mirror Remote to Local (Default/FTP):** Mirrors a remote FTP folder to a local drive path, keeps snapshots, and produces daily zip archives.
2. **Local to Remote (FTP_UPLOAD):** Zips a local folder and uploads the archive to a remote FTP server.

## Requirements

- .NET SDK 10.0+
- Windows machine with access to the remote FTP server
- Local destination on a drive letter path (for example: `D:\Backups\SiteA`)

## Installation

1. Build and publish the service:

```powershell
dotnet publish .\BackupService.csproj -c Release -o C:\Services\BackupService
```

2. Create the Windows service:

```powershell
sc.exe create BackupService binPath= "C:\Services\BackupService\BackupService.exe"
```

3. (Optional) Set the service account if needed:

```powershell
sc.exe config BackupService obj= ".\ServiceUser" password= "YourPassword"
```

4. Start the service:

```powershell
sc.exe start BackupService
```

## Configuration

Edit `appsettings.json` (development) or provide a matching
`appsettings.json` alongside the published executable. The important
sections are `BackupOptions`, `FileLogging`, and `ServiceSettings`.

Key settings:

- `BackupOptions:RunAt`: local time for daily run (default `02:00`).
- `BackupOptions:DefaultTimeoutMinutes`: per-job timeout (default `60`).
- `BackupOptions:HistoryCopies`: number of snapshots kept (default `5`).
- `BackupOptions:Backups`: list of backup jobs.

### Backup Types

- `FTP` (Default): Mirrors remote files to local storage.
- `FTP_UPLOAD`: Zips a local folder and uploads it to the FTP server.
- `HTTP`: Triggers a backup via an HTTP endpoint and downloads the result.

### Job Settings

Each backup job supports:

- `Name`: friendly name for logs.
- `BackupType`: `FTP`, `FTP_UPLOAD`, or `HTTP`.
- `Host`, `Port`, `Username`, `Password`: FTP server credentials.
- `RemotePath`: 
    - For `FTP`: remote folder to mirror.
    - For `FTP_UPLOAD`: remote directory where the zip will be uploaded.
- `LocalPath`: 
    - For `FTP`: local folder to store the mirror, snapshots, and archives.
    - For `FTP_UPLOAD`: local folder to be zipped and uploaded.
- `Encryption`: `Explicit` or `Implicit` (default `Explicit`).
- `Passive`: `true` for passive mode (default `true`).
- `AllowInvalidCertificate`: set `true` to skip TLS validation.
- `TimeoutMinutes`: overrides the default timeout.
- `RetentionDays`: days to keep local zip archives (for `FTP` mode, default `7`).

Example Configuration:

```json
{
  "BackupOptions": {
    "RunAt": "02:00",
    "Backups": [
      {
        "Name": "MirrorRemoteSite",
        "BackupType": "FTP",
        "Host": "ftp.example.com",
        "LocalPath": "D:\\Backups\\SiteA",
        "RemotePath": "/www"
      },
      {
        "Name": "UploadLocalData",
        "BackupType": "FTP_UPLOAD",
        "Host": "backup.storage.com",
        "LocalPath": "C:\\ImportantData",
        "RemotePath": "/backups/server1",
        "Username": "backup_user",
        "Password": "secure_password"
      }
    ]
  }
}
```

### Sensitive values

Passwords are stored in clear text in `appsettings.json`. For production
use, consider setting credentials in environment variables and leaving
the file values blank:

```powershell
$env:BackupOptions__Backups__0__Username = "user"
$env:BackupOptions__Backups__0__Password = "password"
```

## Running behavior

- On startup, the service performs a one-off FTP test run using the first
  configured backup job and writes progress to the console.
- The service runs once per day at the configured time.
- Backups execute sequentially; a failure does not block the next job.
- Each job is cancelled if it exceeds its timeout.
- The remote path is mirrored into `LocalPath\current`.
- Snapshot copies are stored in `LocalPath\_history\yyyyMMdd_HHmmss`.
- A daily zip archive is created at
  `LocalPath\archives\<JobName>\yyyy-MM-dd.zip`, and archives older than
  `RetentionDays` are deleted.
- Logs are written to the Windows Event Log and to the file path in
  `FileLogging:Path`.
  
Note: `LocalPath` must be a local drive path (for example `D:\Backups\SiteA`);
UNC paths are rejected by the runner.

## Running locally (console mode)

You can run the worker as a console app for quick testing:

```powershell
dotnet run --project .\BackupService.csproj
```

Press Ctrl+C to stop it.

## Uninstall

```powershell
sc.exe stop BackupService
sc.exe delete BackupService
```

## Troubleshooting

- If the service cannot write logs, ensure the service account has write
  access to the log directory and backup destination.
- If TLS validation fails, set `AllowInvalidCertificate` to `true` for
  the specific backup or install a trusted certificate on the machine.
- If Event Log entries do not appear, run the service once with elevated
  permissions or pre-create the event source.
