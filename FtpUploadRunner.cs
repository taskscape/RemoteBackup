using FluentFTP;
using System.IO.Compression;

namespace BackupService;

public class FtpUploadRunner(ILogger<FtpUploadRunner> logger)
{
    public async Task<bool> RunJobAsync(
        BackupJobOptions job,
        BackupOptions options,
        CancellationToken cancellationToken)
    {
        string? zipFilePath = null;
        try
        {
            if (string.IsNullOrWhiteSpace(job.Host))
            {
                logger.LogError("Backup '{name}' is missing Host.", job.Name);
                return false;
            }

            if (string.IsNullOrWhiteSpace(job.LocalPath))
            {
                logger.LogError("Backup '{name}' is missing LocalPath.", job.Name);
                return false;
            }

            if (!Directory.Exists(job.LocalPath))
            {
                logger.LogError("Backup '{name}' LocalPath '{path}' does not exist.", job.Name, job.LocalPath);
                return false;
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "BackupService", job.Name);
            Directory.CreateDirectory(tempDir);

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
            var zipFileName = $"{job.Name}_{timestamp}.zip";
            zipFilePath = Path.Combine(tempDir, zipFileName);

            logger.LogInformation("Zipping folder '{path}' for backup '{name}'...", job.LocalPath, job.Name);
            
            if (File.Exists(zipFilePath))
            {
                File.Delete(zipFilePath);
            }

            // ZipFile.CreateFromDirectory is synchronous, running it in a Task.Run to allow for cancellation check if needed
            // though the ZIP operation itself won't stop immediately unless it checks cancellation.
            await Task.Run(() => ZipFile.CreateFromDirectory(job.LocalPath, zipFilePath, CompressionLevel.Optimal, false), cancellationToken);

            var fileInfo = new FileInfo(zipFilePath);
            logger.LogInformation("Created zip archive: {path} ({size})", zipFilePath, FormatBytes(fileInfo.Length));

            logger.LogInformation("Connecting to {host}:{port} for backup '{name}'...", job.Host, job.Port, job.Name);

            using var client = new AsyncFtpClient(job.Host, job.Username, job.Password, job.Port);
            client.Config.EncryptionMode = ParseEncryptionMode(job.Encryption);
            client.Config.DataConnectionType = FtpDataConnectionType.PASV;
            client.Config.ConnectTimeout = 120000;
            client.Config.ReadTimeout = 120000;
            client.Config.DataConnectionConnectTimeout = 120000;
            client.Config.DataConnectionReadTimeout = 120000;
            client.Config.ValidateAnyCertificate = job.AllowInvalidCertificate;
            client.Config.DataConnectionEncryption = false; // Disable data channel encryption for better stability on home.pl

            if (!job.AllowInvalidCertificate)
            {
                client.ValidateCertificate += (_, e) =>
                {
                    if (e.PolicyErrors != System.Net.Security.SslPolicyErrors.None)
                    {
                        logger.LogError("Certificate validation failed for backup '{name}': {errors}", job.Name, e.PolicyErrors);
                    }
                };
            }

            await client.Connect(cancellationToken);
            
            var remoteDir = string.IsNullOrWhiteSpace(job.RemotePath) ? "/" : job.RemotePath;
            var remoteFilePath = (remoteDir.EndsWith("/") ? remoteDir + zipFileName : remoteDir + "/" + zipFileName).Replace("\\", "/");

            logger.LogInformation("Uploading backup to '{remotePath}'...", remoteFilePath);
            
            var status = await client.UploadFile(zipFilePath, remoteFilePath, FtpRemoteExists.Overwrite, true, FtpVerify.Retry, 
                new Progress<FtpProgress>(progress =>
                {
                    string totalStr = "?";
                    if (progress.Progress > 0)
                    {
                        long total = (long)(progress.TransferredBytes / (progress.Progress / 100.0));
                        totalStr = FormatBytes(total);
                    }
                    var progressString = $"\r[UPLOAD] {job.Name}: {progress.Progress:F2}% ({FormatBytes(progress.TransferredBytes)} / {totalStr})";
                    Console.Write(progressString);
                }), cancellationToken);

            Console.WriteLine(); // Final newline after progress

            if (status == FtpStatus.Success)
            {
                logger.LogInformation("Backup '{name}' uploaded successfully.", job.Name);
                await client.Disconnect(cancellationToken);
                return true;
            }
            else
            {
                logger.LogError("Backup '{name}' upload failed with status: {status}", job.Name, status);
                await client.Disconnect(cancellationToken);
                return false;
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Backup '{name}' was cancelled.", job.Name);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FTP Upload Backup '{name}' failed.", job.Name);
            return false;
        }
        finally
        {
            if (zipFilePath != null && File.Exists(zipFilePath))
            {
                try
                {
                    File.Delete(zipFilePath);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete temporary zip file '{path}'.", zipFilePath);
                }
            }
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        double dblBytes = bytes;
        while (i < suffixes.Length - 1 && bytes >= 1024)
        {
            i++;
            bytes /= 1024;
            dblBytes /= 1024;
        }
        return $"{dblBytes:F2} {suffixes[i]}";
    }

    private static FtpEncryptionMode ParseEncryptionMode(string? mode)
    {
        if (Enum.TryParse<FtpEncryptionMode>(mode, true, out var parsed))
        {
            return parsed;
        }

        return FtpEncryptionMode.Explicit;
    }
}
