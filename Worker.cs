using Microsoft.Extensions.Options;

namespace BackupService;

public class Worker(
    ILogger<Worker> logger,
    BackupCoordinator coordinator,
    IOptions<BackupOptions> options) : BackgroundService
{
    private readonly BackupOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Backup service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var nextRun = GetNextRunTime(_options.RunAtLocalTime);
            var delay = nextRun - DateTimeOffset.Now;
            if (delay < TimeSpan.Zero)
            {
                delay = TimeSpan.Zero;
            }

            logger.LogInformation(
                "Next backup run scheduled at {time} (in {delay}).",
                nextRun,
                delay);

            await Task.Delay(delay, stoppingToken);

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            logger.LogInformation("Starting backup run at {time}.", DateTimeOffset.Now);
            await coordinator.RunBackupsAsync(stoppingToken);
            logger.LogInformation("Backup run completed at {time}.", DateTimeOffset.Now);
        }
    }

    private static DateTimeOffset GetNextRunTime(TimeSpan runAtLocalTime)
    {
        var now = DateTimeOffset.Now;
        var todayRun = new DateTimeOffset(
            now.Year,
            now.Month,
            now.Day,
            runAtLocalTime.Hours,
            runAtLocalTime.Minutes,
            runAtLocalTime.Seconds,
            now.Offset);

        if (todayRun <= now)
        {
            todayRun = todayRun.AddDays(1);
        }

        return todayRun;
    }
}
