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

        var successfulJobs = new List<string>();
        var failedJobs = new List<string>();
        var startedAt = DateTimeOffset.Now;

        foreach (var job in _options.Backups)
        {
            bool currentJobSuccess = false;
            try
            {
                var timeoutMinutes = job.TimeoutMinutes ?? _options.DefaultTimeoutMinutes;
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(timeoutMinutes));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeoutCts.Token);

                var started = DateTimeOffset.Now;
                var backupType = job.BackupType?.ToUpper() ?? "FTP";

                logger.LogInformation("Starting {type} backup '{name}' with timeout {timeoutMinutes} minutes.", backupType, job.Name, timeoutMinutes);

                try
                {
                    if (backupType == "HTTP")
                    {
                        currentJobSuccess = await httpRunner.RunJobAsync(job, _options, linkedCts.Token);
                    }
                    else if (backupType == "FTP_UPLOAD")
                    {
                        currentJobSuccess = await ftpUploadRunner.RunJobAsync(job, _options, linkedCts.Token);
                    }
                    else
                    {
                        currentJobSuccess = await ftpRunner.RunJobAsync(job, _options, linkedCts.Token);
                    }

                    var duration = DateTimeOffset.Now - started;
                    if (currentJobSuccess)
                    {
                        logger.LogInformation("Backup '{name}' completed successfully in {duration}.", job.Name, duration);
                        successfulJobs.Add(job.Name);
                    }
                    else
                    {
                        var reason = "Check individual step logs for details.";
                        logger.LogError("Backup '{name}' failed ({reason}) after {duration}.", job.Name, reason, duration);
                        failedJobs.Add(job.Name);
                        
                        try { await emailService.SendFailureNotificationAsync(job, reason); }
                        catch (Exception emailEx) { logger.LogError(emailEx, "Failed to send failure email for '{name}'.", job.Name); }
                    }
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
                {
                    var reason = $"Timed out after {timeoutMinutes} minutes.";
                    logger.LogError("Backup '{name}' failed! Reason: {reason}", job.Name, reason);
                    failedJobs.Add(job.Name);
                    
                    try { await emailService.SendFailureNotificationAsync(job, reason); }
                    catch (Exception emailEx) { logger.LogError(emailEx, "Failed to send failure email for '{name}'.", job.Name); }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Backup '{name}' failed with unexpected exception! Reason: {message}", job.Name, ex.Message);
                    failedJobs.Add(job.Name);
                    
                    try { await emailService.SendFailureNotificationAsync(job, ex.Message, ex); }
                    catch (Exception emailEx) { logger.LogError(emailEx, "Failed to send failure email for '{name}'.", job.Name); }
                }
            }
            catch (Exception criticalEx)
            {
                logger.LogCritical(criticalEx, "Critical error during setup for backup job '{name}'. Skipping to next job.", job.Name);
                failedJobs.Add(job.Name);
            }

            if (stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Backup run aborted due to service shutdown.");
                break;
            }
        }

        var totalDuration = DateTimeOffset.Now - startedAt;
        
        // Log to file/event log
        logger.LogInformation("=== BACKUP RUN SUMMARY ===");
        logger.LogInformation("Total backups processed: {total}", _options.Backups.Count);
        logger.LogInformation("Successful: {count} ({names})", successfulJobs.Count, string.Join(", ", successfulJobs));
        
        // Output to Console for manual runs
        Console.WriteLine("\n" + new string('=', 40));
        Console.WriteLine("        BACKUP RUN SUMMARY");
        Console.WriteLine(new string('-', 40));
        Console.WriteLine($"Total backups processed: {_options.Backups.Count}");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Successful: {successfulJobs.Count}");
        if (successfulJobs.Count > 0) Console.WriteLine($" -> {string.Join(", ", successfulJobs)}");
        Console.ResetColor();

        if (failedJobs.Count > 0)
        {
            logger.LogError("Failed: {count} ({names})", failedJobs.Count, string.Join(", ", failedJobs));
            
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Failed:     {failedJobs.Count}");
            Console.WriteLine($" -> {string.Join(", ", failedJobs)}");
            Console.ResetColor();
        }
        else
        {
            logger.LogInformation("Failed: 0");
            Console.WriteLine("Failed:     0");
        }
        
        logger.LogInformation("Total duration: {duration}", totalDuration);
        logger.LogInformation("==========================");

        Console.WriteLine(new string('-', 40));
        Console.WriteLine($"Total duration: {totalDuration:hh\\:mm\\:ss}");
        Console.WriteLine(new string('=', 40) + "\n");

        return failedJobs.Count == 0;
    }
}
