namespace BackupService;

public class FileLoggerOptions
{
    public const string SectionName = "FileLogging";

    public string Path { get; set; } = "logs\\backup.log";
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;
    public int RetentionDays { get; set; } = 7;
}
