# BackupService

Windows service for scheduled FTP/FTPS backups. It mirrors a remote FTP
folder to a local drive path on a daily schedule, keeps timestamped
snapshots, and produces daily zip archives.

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
- `ServiceSettings:StartHour` / `ServiceSettings:StartMinute`: currently
  used by a startup timer that logs when the service would start; actual
  scheduling uses `BackupOptions:RunAt`.

Each backup job supports:

- `Name`: friendly name for logs.
- `Host`, `Port`, `Username`, `Password`: FTP server credentials.
- `RemotePath`: remote folder to mirror.
- `LocalPath`: local drive folder to store the mirror, snapshots, and archives.
- `Encryption`: `Explicit` or `Implicit` (default `Explicit`).
- `Passive`: `true` for passive mode (default `true`).
- `AllowInvalidCertificate`: set `true` to skip TLS validation.
- `TimeoutMinutes`: overrides the default timeout.
- `HistoryCopies`: overrides the default retention count.
- `RetentionDays`: days to keep zip archives (default `7`).
- `OperationTimeoutMinutes`: timeout for the final post-archive FTP
  download (default `10`).
- `CompletionTimeoutMinutes`: overall per-job timeout for copy/archive
  work (default `180`).
- `Mode`: backup mode: `Local` or `Remote` (default `Local`). `ArchivesOnly` is still accepted for compatibility and behaves as `Local`.
- `RemoteTriggerUrl`: optional HTTP endpoint to request server-side zipping
  (used with `RemoteZip` mode). The endpoint should return a `Location`
  URL (or JSON with `{"location":"..."}`) where the resulting archive
  will be available for download. **(kept for compatibility)**
- `RemoteTriggerUrls`: **new** – list of one or more endpoints to trigger
  sequentially. Useful when you need to kick off several independent backups
  (e.g. files first, then database). Each URL can return its own archive location.
  If this list is non-empty the single `RemoteTriggerUrl` is ignored.
- `RemoteTriggerPollIntervalSeconds`: polling interval when waiting for remote
  archive (default `5` seconds).
- `RemoteTriggerTimeoutSeconds`: max seconds to wait for remote archive
  after triggering (default `600` seconds).
- `HttpRetryCount`: number of retry attempts on transient HTTP failures when
  contacting trigger URLs or downloading archives (default `3`).
- `HttpRetryDelaySeconds`: base delay in seconds between retries; each
  attempt waits `attempt * delay` (default `5`).
Example:

```json
{
  "FileLogging": {
    "Path": "logs\\backup.log",
    "MinimumLevel": "Information"
  },
  "BackupOptions": {
    "RunAt": "02:00",
    "HistoryCopies": 5,
    "DefaultTimeoutMinutes": 60,
    "CurrentSubdirectoryName": "current",
    "HistorySubdirectoryName": "_history",
    "Backups": [
      {
        "Name": "SiteA",
        "Host": "ftp.example.com",
        "Port": 21,
        "Username": "user",
        "Password": "password",
        "RemotePath": "/",
        "LocalPath": "D:\\Backups\\SiteA",
        "Encryption": "Explicit",
        "Passive": true,
        "AllowInvalidCertificate": false,
        "TimeoutMinutes": 60,
        "HistoryCopies": 5,
        "RetentionDays": 7,
        "OperationTimeoutMinutes": 10,
        "CompletionTimeoutMinutes": 180,
        "RemoteTriggerUrls": [
          "http://localhost:5000/trigger/files",
          "http://localhost:5000/trigger/database"
        ],
        "HttpRetryCount": 3,
        "HttpRetryDelaySeconds": 5
      }
    ]
  },
  "ServiceSettings": {
    "StartHour": 2,
    "StartMinute": 0
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
- **Local mode**: mirrors the entire remote directory to a temporary folder, then creates a dated zip archive at `LocalPath\yyyy-MM-dd.zip`. (This also covers the previous ArchivesOnly case by downloading existing `.zip` files when present.)
  *Configurations using the old `ArchivesOnly` mode are still supported; the value is now treated as `Local`.*
- **Remote mode**: optionally POSTs to one or more `RemoteTriggerUrl`/`RemoteTriggerUrls` endpoints to request a server-side archive (or archives). When a list is provided, each URL is invoked sequentially; the runner will poll and download each returned location, allowing e.g. a files backup followed by a database dump. If no triggers succeed the service falls back to FTP `.zip` retrieval and then to a local mirror.
- Daily zip archives are stored directly in `LocalPath` with filenames
  `yyyy-MM-dd.zip`, and archives older than `RetentionDays` are deleted.
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

## Remote trigger endpoint (new)

I added a minimal example endpoint that can be deployed on target servers to
support the `RemoteZip` mode. The endpoint accepts a `POST /trigger` JSON
body, starts a background job that creates a zip archive from a local folder,
stores the archive under `wwwroot/archives/` and returns `202 Accepted` with
the `Location` header set to `/archives/{filename}`. `FtpBackupRunner` can
POST to this URL and poll for the archive using `HEAD` as implemented.

Files added:
- `RemoteTriggerEndpoint/RemoteTriggerEndpoint.csproj`
- `RemoteTriggerEndpoint/Program.cs` (minimal API exposing `POST /trigger`)
- `RemoteTriggerEndpoint/ZipWorker.cs` (background worker that creates ZIPs)

Quick start (from repository root):

```powershell
dotnet run --project .\RemoteTriggerEndpoint -p:ASPNETCORE_URLS="http://0.0.0.0:5000"
```

Example `curl` to trigger zipping (run on the server or point `RemoteTriggerUrl` from the backup service to this server):

```bash
curl -X POST http://localhost:5000/trigger -H "Content-Type: application/json" \
  -d '{"sourcePath":"C:\\path\\to\\data","archiveName":"siteA-backup.zip","overwrite":true}' -i
```

Response:
- `202 Accepted` with `Location: /archives/siteA-backup.zip` — the background job is creating the archive.
- `HEAD /archives/siteA-backup.zip` will return `200` when the file is ready; otherwise `404`.

If you'd like, I can:
- add authentication to the endpoint (API key / basic) for security,
- add retention/cleanup for server-side archives, or
- create a lightweight PowerShell script alternative instead of ASP.NET Core.
