using FluentFTP;
using System.Globalization;
using System.IO.Compression;
using System.Net;


namespace BackupService;

public class FtpBackupRunner(ILogger<FtpBackupRunner> logger)
{
    public async Task RunJobAsync(
        BackupJobOptions job,
        BackupOptions options,
        CancellationToken cancellationToken)
    {
        using var completionCts =
            new CancellationTokenSource(TimeSpan.FromMinutes(job.CompletionTimeoutMinutes));

        using var linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                completionCts.Token);

        var completionToken = linkedCts.Token;

        try
        {

            if (string.IsNullOrWhiteSpace(job.Host))
            {
                logger.LogError("Backup '{name}' is missing Host.", job.Name);
                return;
            }

            if (string.IsNullOrWhiteSpace(job.LocalPath))
            {
                logger.LogError("Backup '{name}' is missing LocalPath.", job.Name);
                return;
            }

            if (!IsLocalDrivePath(job.LocalPath))
            {
                logger.LogError(
                    "Backup '{name}' LocalPath '{path}' is not a local drive path.",
                    job.Name,
                    job.LocalPath);
                return;
            }

            var currentRoot = Path.Combine(
                job.LocalPath,
                options.CurrentSubdirectoryName);
            var historyRoot = Path.Combine(
                job.LocalPath,
                options.HistorySubdirectoryName);

            Directory.CreateDirectory(currentRoot);
            Directory.CreateDirectory(historyRoot);

            using var client = new FtpClient(job.Host, job.Username, job.Password, job.Port);

            client.Config.EncryptionMode = ParseEncryptionMode(job.Encryption);
            client.Config.DataConnectionType = job.Passive
                ? FtpDataConnectionType.PASV
                : FtpDataConnectionType.PORT;
            client.Config.DataConnectionEncryption = true;
            client.Config.ConnectTimeout = 15000;
            client.Config.ReadTimeout = 15000;
            client.Config.DataConnectionConnectTimeout = 15000;
            client.Config.DataConnectionReadTimeout = 15000;
            client.Config.ValidateAnyCertificate = job.AllowInvalidCertificate;

            if (!job.AllowInvalidCertificate)
            {
                client.ValidateCertificate += (_, e) =>
                {
                    if (e.PolicyErrors != System.Net.Security.SslPolicyErrors.None)
                    {
                        logger.LogError(
                            "Certificate validation failed for backup '{name}': {errors}",
                            job.Name,
                            e.PolicyErrors);
                    }
                };
            }

            var remotePath = string.IsNullOrWhiteSpace(job.RemotePath)
                ? "/"
                : job.RemotePath;

            logger.LogInformation(
                "Connecting to {host}:{port} for backup '{name}'.",
                job.Host,
                job.Port,
                job.Name);

            client.Connect();
            logger.LogInformation(
                "Connected to {host}. Starting mirror of {remotePath}.",
                job.Host,
                remotePath);

            var results = client.DownloadDirectory(
                currentRoot,
                remotePath,
                FtpFolderSyncMode.Mirror,
                FtpLocalExists.Overwrite,
                FtpVerify.None);

            LogResults(job.Name, results);

            var snapshotName = DateTimeOffset.Now
                .ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var snapshotPath = Path.Combine(historyRoot, snapshotName);

            logger.LogInformation(
                "Creating history snapshot for backup '{name}' at {path}.",
                job.Name,
                snapshotPath);


            CopyDirectory(currentRoot, snapshotPath, completionToken);
            CleanupHistory(historyRoot, job, options);
            
            string currentDir = Path.Combine(job.LocalPath, "current");
            Directory.CreateDirectory(currentDir);
            
            CopyDirectory(snapshotPath, currentDir, completionToken);

            string archiveDir = Path.Combine(job.LocalPath, "archives", job.Name);
            Directory.CreateDirectory(archiveDir);

            string zipPath = Path.Combine(archiveDir, DateTime.Now.ToString("yyyy-MM-dd") + ".zip");

            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            ZipFile.CreateFromDirectory(currentDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

            foreach (var file in Directory.GetFiles(archiveDir, "*.zip"))
            {
                var creationDate = File.GetCreationTime(file);
                if ((DateTime.Now - creationDate).TotalDays > job.RetentionDays)
                    File.Delete(file);
            }

            foreach (var file in Directory.GetFiles(currentDir))
                File.Delete(file);

            foreach (var dir in Directory.GetDirectories(currentDir))
                Directory.Delete(dir, recursive: true);
            
            using var operationCts = new CancellationTokenSource(TimeSpan.FromMinutes(job.OperationTimeoutMinutes));
            using var opLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(completionToken, operationCts.Token);

            string host = job.Host;
            string username = job.Username;
            string password = job.Password;
            var ftpClient = new FtpClient();
            ftpClient.Host = host;
            ftpClient.Credentials = new NetworkCredential(username, password);
            ftpClient.Connect();
            
            string localPath = Path.Combine(job.LocalPath, "current");
            ftpClient.DownloadDirectory(remotePath, localPath);
            ftpClient.Disconnect();
        }   
        catch (OperationCanceledException)
        {
            logger.LogWarning("Backup '{name}' cancelled.", job.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Backup '{name}' failed.", job.Name);
        }
    }


private static bool IsLocalDrivePath(string path)
    {
        if (!Path.IsPathRooted(path))
        {
            return false;
        }

        var root = Path.GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        if (root.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return false;
        }

        return root.Contains(':');
    }

    private static FtpEncryptionMode ParseEncryptionMode(string? mode)
    {
        if (Enum.TryParse<FtpEncryptionMode>(mode, true, out var parsed))
        {
            return parsed;
        }

        return FtpEncryptionMode.Explicit;
    }

    private void LogResults(string jobName, List<FtpResult> results)
    {
        var failed = results.Count(r => !r.IsSuccess);
        if (failed == 0)
        {
            logger.LogInformation(
                "Backup '{name}' downloaded {count} items.",
                jobName,
                results.Count);
            return;
        }

        logger.LogWarning(
            "Backup '{name}' downloaded {count} items with {failed} failures.",
            jobName,
            results.Count,
            failed);

        foreach (var result in results.Where(r => !r.IsSuccess))
        {
            logger.LogWarning(
                "Backup '{name}' failed item {item} with exception {message}.",
                jobName,
                result.Name,
                result.Exception?.Message);
        }
    }

    public static async Task CopyDirectory(
        string sourceDir,
        string targetDir,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetFile = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, targetFile, overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directoryName = Path.GetFileName(directory);
            var targetSubdir = Path.Combine(targetDir, directoryName);
            //CopyDirectory(directory, targetSubdir, cancellationToken);
            await CopyDirectory(directory, targetSubdir, cancellationToken);
            
        }
    }

    private void CleanupHistory(
        string historyRoot,
        BackupJobOptions job,
        BackupOptions options)
    {
        var keep = job.HistoryCopies ?? options.HistoryCopies;
        if (keep <= 0)
        {
            return;
        }

        var snapshots = new DirectoryInfo(historyRoot)
            .GetDirectories()
            .OrderByDescending(d => d.Name)
            .ToList();

        if (snapshots.Count <= keep)
        {
            return;
        }

        foreach (var old in snapshots.Skip(keep))
        {
            try
            {
                old.Delete(recursive: true);
                logger.LogInformation(
                    "Deleted old history snapshot {snapshot} for backup '{name}'.",
                    old.FullName,
                    job.Name);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to delete old history snapshot {snapshot} for backup '{name}'.",
                    old.FullName,
                    job.Name);
            }
        }
    }
}
