# BackupService

Windows service for scheduled FTP/FTPS and HTTP/PHP backups. It mirrors remote
files to a local drive path on a daily schedule, keeps timestamped
snapshots, and produces daily archives.

## Features

- **FTP/FTPS Backup**: Classic file mirroring from FTP servers.
- **HTTP/PHP Backup**: High-performance backup for large sites (thousands of files).
  - Server-side ZIP compression (much faster than individual FTP downloads).
  - Integrated MySQL database backup with table prefix support.
  - Token-based security.
  - Automatic cleanup of old archives on both server and local machine.

## Requirements

- .NET SDK 10.0+ (for building)
- Windows machine to run the service
- (For HTTP mode) PHP 7.4+ on the remote server with `ZipArchive` and `mysqli` extensions.

## Installation

1. Build and publish the service:
```powershell
dotnet publish .\BackupService.csproj -c Release -o C:\Services\BackupService
```

2. Create the Windows service:
```powershell
sc.exe create BackupService binPath= "C:\Services\BackupService\BackupService.exe"
```

3. Start the service:
```powershell
sc.exe start BackupService
```

## Configuration

### 1. HTTP/PHP Mode (Recommended for Web Services)

This mode uses a PHP script on your server to bundle files and database into archives for the C# service to download.

#### Server Setup:
1. Copy the contents of the `php-server/` folder to your website (e.g., to `/backup/`).
2. Edit `php-server/config.php`:
   - Set `auth_token` to a secure random string.
   - Configure `db` section with your MySQL credentials.
   - Adjust `fs -> source_dir` to point to your website root.
   - Ensure `backup` folder is in `exclude_dirs`.

#### Client Setup (`appsettings.json`):
```json
{
  "BackupOptions": {
    "Backups": [
      {
        "Name": "MyWebsite",
        "BackupType": "HTTP",
        "EndpointUrl": "https://yourdomain.com/backup/php-server/backup.php",
        "ApiToken": "your_secure_token",
        "LocalPath": "C:\\Backups\\MyWebsite",
        "RetentionDays": 7
      }
    ]
  }
}
```

### 2. FTP/FTPS Mode

Classic mode for servers without PHP support.

```json
{
  "BackupOptions": {
    "Backups": [
      {
        "Name": "LegacyFTPSite",
        "BackupType": "FTP",
        "Host": "ftp.example.com",
        "Username": "user",
        "Password": "password",
        "RemotePath": "/",
        "LocalPath": "C:\\Backups\\LegacySite",
        "Encryption": "Explicit",
        "Passive": true
      }
    ]
  }
}
```

## Global Settings

- `BackupOptions:RunAt`: Local time for daily run (e.g., `"02:00"`).
- `BackupOptions:HistoryCopies`: Number of snapshots kept (for FTP mode).
- `FileLogging:Path`: Location of service logs.

## Running Behavior

- **Immediate Test**: On startup, the service performs a test run of the **first** configured backup job.
- **Scheduled Run**: The service runs once per day at the `RunAt` time.
- **HTTP Flow**:
  1. Requests DB backup (`?action=db`).
  2. Downloads the generated `.sql` file.
  3. Requests Files backup (`?action=files`).
  4. Downloads the generated `.zip` archive.
  5. Cleans up old local and remote archives.

## Troubleshooting

- **HTTP 500 Error**: Check if `ZipArchive` and `mysqli` extensions are enabled in your PHP configuration. Check `error_log` on the server.
- **Unauthorized**: Ensure `ApiToken` in C# matches `auth_token` in `config.php`.
- **Permission Denied**: Ensure the PHP script has write access to its `archives/` directory.

## Uninstall

```powershell
sc.exe stop BackupService
sc.exe delete BackupService
```
