# RemoteBackup Service

Advanced Windows service for automated scheduled backups. The system supports three primary modes of operation:
1. FTP Mirror (Remote to Local): Synchronizes a remote FTP folder to a local disk, maintains history snapshots, and compresses them into ZIP archives.
2. FTP Upload (Local to Remote): Compresses a local folder into a ZIP archive and uploads it to a remote FTP server.
3. HTTP Trigger (Trigger and Download): Calls a specific URL that generates a backup on the server, then downloads the resulting file locally.

---

## Configuration (appsettings.json)

The configuration is located within the "BackupOptions" section. You can define multiple independent backup tasks in the "Backups" array.

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
- **API_TOKEN**: A secret string that must match the `ApiToken` in your `appsettings.json`. If they don't match, the request is rejected.
- **ALLOWED_IPS**: An array of IP addresses allowed to trigger the backup. Use `['*']` to allow any IP (not recommended) or specify your backup server's IP.
- **BACKUP_SOURCE_DIR**: The absolute path to the folder on the web server that you want to back up (e.g., `/var/www/html`).
- **TEMP_DIR**: Where the temporary ZIP file is created before downloading.

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

## Installation and Logging

### How to Install
1. Run RemoteBackupSetup.exe as administrator.
2. The service will be automatically registered and started as "RemoteBackup Service".
3. After installation, appsettings.json will open – configure your credentials there.

### Where to Find Logs
If a backup fails, check the following:
1. Installation Folder: logs/backup.log file.
2. Windows Event Viewer: "Application" section, source "BackupService".

---
Documentation created for RemoteBackup Service v1.0.0
