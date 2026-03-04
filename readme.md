# RemoteBackup Service

Advanced Windows service for automated scheduled backups. The system supports three primary modes of operation:
1. FTP Mirror (Remote to Local): Mirrors a remote FTP folder to a local disk, maintains history snapshots, and compresses them into ZIP archives.
2. FTP Upload (Local to Remote): Compresses a local folder into a ZIP archive and uploads it to a remote FTP server.
3. HTTP Trigger (Trigger and Download): Calls a specific URL that generates a backup on the server, then downloads the resulting file locally.

## Requirements
- .NET SDK 10.0+
- Windows machine with access to the remote servers
- Local destination on a drive letter path (for example: D:\Backups\SiteA)
- Administrator privileges for installation

---

## Configuration (appsettings.json)

The configuration is located within the "BackupOptions" section. You can define multiple independent backup tasks in the "Backups" array. The important sections are `BackupOptions`, `FileLogging`, and `ServiceSettings`.

### Global Settings
| Field | Description | Example |
| :--- | :--- | :--- |
| RunAt | Time of day to start the backup (24h format). | "02:30" |
| HistoryCopies | Default number of historical archives to keep. | 5 |
| DefaultTimeoutMinutes | Default timeout for a single backup task. | 60 |
| Backups | List (array) of objects defining individual tasks. | [...] |

---

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

#### HTTP Configuration (for "HTTP" type)
- EndpointUrl: Full URL to trigger the backup (e.g., https://api.site.com/backup.php).
- ApiToken: A separate field for the authorization token or secret key.

---

## Server-Side Setup (PHP)

The `php-server` folder contains a ready-to-use script for the HTTP backup mode. This is useful if you want to trigger a backup of a web server from the Windows service.

### Files in php-server:
1. **backup.php**: The main execution script. When called with a valid token, it zips the configured source directory and streams it to the client.
2. **config.php**: Security and path configuration.

### config.php Breakdown:
Modify these values to secure your endpoint:
- **API_TOKEN**: A secret string that must match the `ApiToken` in your `appsettings.json`.
- **ALLOWED_IPS**: An array of IP addresses allowed to trigger the backup.
- **BACKUP_SOURCE_DIR**: The absolute path to the folder on the web server to back up.
- **TEMP_DIR**: Where the temporary ZIP file is created before downloading.

---

## Sensitive values
Passwords are stored in clear text in appsettings.json. For production use, consider setting credentials in environment variables and leaving the file values blank:
```powershell
$env:BackupOptions__Backups__0__Username = "user"
$env:BackupOptions__Backups__0__Password = "password"
```

---

## Running behavior
- **Startup Test**: On startup, the service performs a one-off FTP test run using the first configured backup job and writes progress to the console.
- **Daily Schedule**: The service runs once per day at the configured `RunAt` time.
- **Sequential Execution**: Backups execute sequentially; a failure does not block the next job.
- **Timeouts**: Each job is cancelled if it exceeds its timeout.
- **Folder Structure**:
    - The remote path is mirrored into `LocalPath\current`.
    - Snapshot copies are stored in `LocalPath\_history\yyyyMMdd_HHmmss`.
    - A daily zip archive is created at `LocalPath\archives\<JobName>\yyyy-MM-dd.zip`, and archives older than `RetentionDays` are deleted.
- **Logging**: Logs are written to the Windows Event Log and to the file path in `FileLogging:Path`.

---

## Configuration Examples

Copy and paste the following into your appsettings.json and adjust the values:

```json
{
  "BackupOptions": {
    "RunAt": "01:00",
    "HistoryCopies": 5,
    "Backups": [
      {
        "Name": "Website_Files",
        "BackupType": "FTP",
        "Host": "ftp.example.com",
        "Username": "admin_www",
        "Password": "secure-password-123",
        "RemotePath": "/public_html",
        "LocalPath": "D:\\Backups\\Website",
        "RetentionDays": 14
      },
      {
        "Name": "Database_Export",
        "BackupType": "FTP_UPLOAD",
        "Host": "storage-server.com",
        "Username": "backup_user",
        "Password": "storage-password",
        "LocalPath": "C:\\SQLBackups",
        "RemotePath": "/remote-storage/sql",
        "Encryption": "Explicit"
      },
      {
        "Name": "External_API_System",
        "BackupType": "HTTP",
        "EndpointUrl": "https://mysystem.com/api/backup.php",
        "ApiToken": "YOUR_SECRET_TOKEN_HERE",
        "LocalPath": "D:\\Backups\\SystemAPI",
        "HistoryCopies": 3
      }
    ]
  }
}
```

---

## Installation and Uninstallation

### Using the Installer (Recommended)
1. Run `RemoteBackupSetup.exe` as administrator.
2. The service will be automatically registered and started.
3. After installation, `appsettings.json` will open for configuration.

### Manual Installation
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

### Manual Uninstall
```powershell
sc.exe stop BackupService
sc.exe delete BackupService
```

---

## Running locally (console mode)
You can run the worker as a console app for quick testing:
```powershell
dotnet run --project .\BackupService.csproj
```
Press Ctrl+C to stop it.

---

## Troubleshooting
- **Write Access**: If the service cannot write logs, ensure the service account has write access to the log directory and backup destination.
- **TLS Validation**: If TLS validation fails, set `AllowInvalidCertificate` to `true` for the specific backup or install a trusted certificate on the machine.
- **Event Log**: If Event Log entries do not appear, run the service once with elevated permissions or pre-create the event source.
- **UNC Paths**: `LocalPath` must be a local drive path (for example `D:\Backups\SiteA`); UNC paths are rejected by the runner.

---
Documentation created for RemoteBackup Service v1.0.0
