using System.Globalization;

namespace BackupService;

public class BackupOptions
{
    public const string SectionName = "BackupOptions";

    public string RunAt { get; set; } = "02:00";
    public int HistoryCopies { get; set; } = 5;
    public int DefaultTimeoutMinutes { get; set; } = 60;
    public string CurrentSubdirectoryName { get; set; } = "current";
    public string HistorySubdirectoryName { get; set; } = "_history";
    public List<BackupJobOptions> Backups { get; set; } = new();

    public TimeSpan RunAtLocalTime =>
        TimeSpan.TryParse(RunAt, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : TimeSpan.FromHours(2);
}
