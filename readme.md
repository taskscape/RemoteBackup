# RemoteBackup Service

Advanced Windows service for automated scheduled backups. The system supports three primary modes of operation:
1. FTP Mirror (Remote to Local): Synchronizes a remote FTP folder to a local disk, maintains history snapshots, and compresses them into ZIP archives.
2. FTP Upload (Local to Remote): Compresses a local folder into a ZIP archive and uploads it to a remote FTP server.
3. HTTP Trigger (Trigger and Download): Calls a specific URL that generates a backup on the server, then downloads the resulting file locally.

## Requirements

- .NET 10.0 Runtime or SDK
- Windows machine (for Service and Event Log integration)
- Local destination on a drive letter path (e.g., D:\Backups\SiteA)
- Administrator privileges for installation

---

## Configuration (appsettings.json)

The configuration is located within the "BackupOptions" section. You can define multiple independent backup tasks in the "Backups" array.

### Global Settings
| Field | Description | Default |
| :--- | :--- | :--- |
| RunAt | Time of day to start the backup (24h format). | "02:00" |
| HistoryCopies | Default number of historical archives to keep. | 5 |
| DefaultTimeoutMinutes | Default timeout for a single backup task. | 60 |
| CurrentSubdirectoryName | Name of the folder for the latest mirror. | "current" |
| HistorySubdirectoryName | Name of the folder for historical snapshots. | "_history" |
| Backups | List (array) of objects defining individual tasks. | [] |

### Detailed Task Fields (Backups)

Each task in the "Backups" array can contain the following fields:

#### General Settings
- Name (Required): Unique name for the task (used in logs and filenames).
- BackupType: Type of backup: "FTP" (default), "FTP_UPLOAD", or "HTTP".
- LocalPath (Required): Local path on the disk (destination for downloads or source for uploads).
- HistoryCopies: Overrides the global number of kept copies for this specific task.
- RetentionDays: Number of days to keep old ZIP archives (default: 7).
- TimeoutMinutes: Overrides the default timeout for this job.

#### FTP Configuration (for "FTP" and "FTP_UPLOAD" types)
- Host: FTP server address (e.g., ftp.yourdomain.com).
- Port: Server port (default: 21).
- Username: FTP username.
- Password: FTP password.
- RemotePath: Path on the FTP server (source for "FTP", destination for "FTP_UPLOAD").
- Encryption: Encryption type: "None", "Explicit" (default), or "Implicit".
- Passive: Use passive mode (default: true).
- AllowInvalidCertificate: Ignore SSL certificate errors (true/false).
- OperationTimeoutMinutes: Timeout for specific FTP operations (default: 10).
- CompletionTimeoutMinutes: Overall timeout for the entire job including compression (default: 180).

#### HTTP Configuration (for "HTTP" type)
- EndpointUrl: Full URL to trigger the backup (e.g., https://api.site.com/backup.php).
- ApiToken: A separate field for the authorization token or secret key.

### Security and Sensitive Values
Passwords can be stored in clear text in appsettings.json. For production use, consider using environment variables to keep credentials out of the configuration file:
```powershell
$env:BackupOptions__Backups__0__Username = "user"
$env:BackupOptions__Backups__0__Password = "password"
```

---

## Server-Side Setup (PHP)

The `php-server` folder contains a ready-to-use script for the HTTP backup mode.

### Files in php-server:
1. **backup.php**: The main execution script. When called with a valid token, it zips the configured source directory and streams it to the client.
2. **config.php**: Security and path configuration.

### config.php Breakdown:
- **API_TOKEN**: Secret string that must match the ApiToken in appsettings.json.
- **ALLOWED_IPS**: Array of IP addresses allowed to trigger the backup (e.g., ['1.2.3.4']).
- **BACKUP_SOURCE_DIR**: Absolute path to the folder on the web server to back up.
- **TEMP_DIR**: Where the temporary ZIP file is created before downloading.

---

## Running Behavior

- The service runs once per day at the configured `RunAt` time.
- Backups execute sequentially; a failure in one job does not block the next.
- For FTP Mirror:
    - The remote path is mirrored into `LocalPath\current`.
    - Snapshot copies are stored in `LocalPath\_history\yyyyMMdd_HHmmss`.
    - A daily ZIP archive is created in `LocalPath\archives\<JobName>\`.
- Logs are written to the Windows Event Log (Source: BackupService) and to the file defined in `FileLogging:Path`.

---

## Installation and Maintenance

### Using the Installer
1. Run `RemoteBackupSetup.exe` as administrator.
2. The service will be automatically registered and started.
3. After installation, appsettings.json will open for configuration.

### Manual Installation (PowerShell)
```powershell
dotnet publish -c Release -r win-x64 --self-contained -o C:\Services\BackupService
sc.exe create BackupService binPath= "C:\Services\BackupService\BackupService.exe" start= auto
sc.exe start BackupService
```

### Uninstallation
- **Via Control Panel**: Use "Add/Remove Programs".
- **Via PowerShell**:
  ```powershell
  sc.exe stop BackupService
  sc.exe delete BackupService
  ```

---

## Troubleshooting

- **Write Access**: Ensure the service account has write access to both the log directory and the backup destination.
- **TLS/SSL**: If FTP connection fails due to certificate issues, set `AllowInvalidCertificate` to `true`.
- **UNC Paths**: `LocalPath` must be a local drive letter; network UNC paths are not supported by the current runner.
- **Event Log**: If entries don't appear, ensure the service was run at least once with administrator privileges to create the event source.

---
Documentation created for RemoteBackup Service v1.0.0
