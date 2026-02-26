using Microsoft.Extensions.Options;

namespace BackupService;

public class BackupCoordinator(
    ILogger<BackupCoordinator> logger,
    IOptions<BackupOptions> options,
    FtpBackupRunner ftpRunner,
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
            logger.LogInformation(
                "Starting {type} backup '{name}' with timeout {timeoutMinutes} minutes.",
                job.BackupType,
                job.Name,
                timeoutMinutes);

            try
            {
                if (job.BackupType?.ToUpper() == "HTTP")
                {
                    await httpRunner.RunJobAsync(job, _options, linkedCts.Token);
                }
                else
                {
                    await ftpRunner.RunJobAsync(job, _options, linkedCts.Token);
                }

                var duration = DateTimeOffset.Now - started;
                logger.LogInformation(
                    "Backup '{name}' completed in {duration}.",
                    job.Name,
                    duration);
            }
            catch (OperationCanceledException) when (
                timeoutCts.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
            {
                logger.LogError(
                    "Backup '{name}' timed out after {timeoutMinutes} minutes.",
                    job.Name,
                    timeoutMinutes);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Backup '{name}' failed.", job.Name);
            }
        }
    }
}
