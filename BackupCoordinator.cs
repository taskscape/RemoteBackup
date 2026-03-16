using Microsoft.Extensions.Options;

namespace BackupService;

public class BackupCoordinator(
    ILogger<BackupCoordinator> logger,
    IOptions<BackupOptions> options,
    FtpBackupRunner ftpRunner,
    FtpUploadRunner ftpUploadRunner,
    HttpBackupRunner httpRunner)
{
    private readonly BackupOptions _options = options.Value;

    public async Task RunBackupsAsync(CancellationToken stoppingToken)
    {
        if (_options.Backups.Count == 0)
        {
            logger.LogWarning("No backups configured. Skipping run.");
            return;
        }

        foreach (var job in _options.Backups)
        {
            var timeoutMinutes = job.TimeoutMinutes ?? _options.DefaultTimeoutMinutes;
            using var timeoutCts = new CancellationTokenSource(
                TimeSpan.FromMinutes(timeoutMinutes));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                stoppingToken,
                timeoutCts.Token);

            var started = DateTimeOffset.Now;
            var backupType = job.BackupType?.ToUpper() ?? "FTP";
            
            logger.LogInformation(
                "Starting {type} backup '{name}' with timeout {timeoutMinutes} minutes.",
                backupType,
                job.Name,
                timeoutMinutes);

            try
            {
                bool success;
                if (backupType == "HTTP")
                {
                    success = await httpRunner.RunJobAsync(job, _options, linkedCts.Token);
                }
                else if (backupType == "FTP_UPLOAD")
                {
                    success = await ftpUploadRunner.RunJobAsync(job, _options, linkedCts.Token);
                }
                else
                {
                    success = await ftpRunner.RunJobAsync(job, _options, linkedCts.Token);
                }

                var duration = DateTimeOffset.Now - started;
                if (success)
                {
                    logger.LogInformation(
                        "Backup '{name}' completed successfully in {duration}.",
                        job.Name,
                        duration);
                }
                else
                {
                    logger.LogError(
                        "Backup '{name}' failed (check individual step logs for details) after {duration}.",
                        job.Name,
                        duration);
                }
            }
            catch (OperationCanceledException) when (
                timeoutCts.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
            {
                logger.LogError(
                    "Backup '{name}' failed! Reason: Timed out after {timeoutMinutes} minutes.",
                    job.Name,
                    timeoutMinutes);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Backup '{name}' failed! Reason: {message}", job.Name, ex.Message);
            }
        }
    }
}
