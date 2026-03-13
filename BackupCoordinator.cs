using Microsoft.Extensions.Options;

namespace BackupService;

public class BackupCoordinator(
    ILogger<BackupCoordinator> logger,
    IOptions<BackupOptions> options,
    FtpBackupRunner ftpRunner,
    FtpUploadRunner ftpUploadRunner,
    HttpBackupRunner httpRunner,
    EmailNotificationService emailService)
{
    private readonly BackupOptions _options = options.Value;

    public async Task<bool> RunBackupsAsync(CancellationToken stoppingToken)
    {
        if (_options.Backups.Count == 0)
        {
            logger.LogWarning("No backups configured. Skipping run.");
            return true;
        }

        bool allSuccessful = true;
        foreach (var job in _options.Backups)
        {
            // ... (rest of the setup logic remains same)
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
                    allSuccessful = false;
                    var reason = "Check individual step logs for details.";
                    logger.LogError(
                        "Backup '{name}' failed ({reason}) after {duration}.",
                        job.Name,
                        reason,
                        duration);
                    await emailService.SendFailureNotificationAsync(job, reason);
                }
            }
            catch (OperationCanceledException) when (
                timeoutCts.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
            {
                allSuccessful = false;
                var reason = $"Timed out after {timeoutMinutes} minutes.";
                logger.LogError(
                    "Backup '{name}' failed! Reason: {reason}",
                    job.Name,
                    reason);
                await emailService.SendFailureNotificationAsync(job, reason);
            }
            catch (Exception ex)
            {
                allSuccessful = false;
                logger.LogError(ex, "Backup '{name}' failed! Reason: {message}", job.Name, ex.Message);
                await emailService.SendFailureNotificationAsync(job, ex.Message, ex);
            }
        }
        return allSuccessful;
    }
}
