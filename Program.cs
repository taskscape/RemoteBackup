using BackupService;
using Microsoft.Extensions.Logging.EventLog;

var builder = Host.CreateApplicationBuilder(args);
var backupOptions = builder.Configuration.GetSection("BackupOptions").Get<BackupOptions>();

    if (!backupOptions.Backups.Any())
    {
        Console.WriteLine("No backups found in configuration!");
        return; 
    }

var backupJob = backupOptions.Backups.First();

builder.Services.Configure<BackupOptions>(
    builder.Configuration.GetSection(BackupOptions.SectionName));
builder.Services.Configure<FileLoggerOptions>(
    builder.Configuration.GetSection(FileLoggerOptions.SectionName));

builder.Services.AddSingleton<FtpBackupRunner>();
builder.Services.AddSingleton<FtpUploadRunner>();
builder.Services.AddSingleton<HttpBackupRunner>();
builder.Services.AddSingleton<BackupCoordinator>();
builder.Services.AddHostedService<Worker>();

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "BackupService";
});

builder.Logging.ClearProviders();
builder.Logging.AddEventLog(new EventLogSettings
{
    LogName = "Application",
    SourceName = "BackupService"
});

builder.Logging.AddFileLogger(
    builder.Configuration.GetSection(FileLoggerOptions.SectionName));

int starHour = builder.Configuration.GetValue<int>("ServiceSettings:StartHour");
int startMinute = builder.Configuration.GetValue<int>("ServiceSettings:StartMinute");
ServiceScheduler.StartServiceAtConfiguredTime(starHour, startMinute, () =>
{
    Console.WriteLine("The timer worked, the service starts!");
});

var host = builder.Build();

var ftpRunner = host.Services.GetRequiredService<FtpBackupRunner>();
var ftpUploadRunner = host.Services.GetRequiredService<FtpUploadRunner>();
var httpRunner = host.Services.GetRequiredService<HttpBackupRunner>();

try
{
    var backupType = backupJob.BackupType?.ToUpper() ?? "FTP";
    bool success;
    if (backupType == "HTTP")
    {
        Console.WriteLine($"Starting HTTP backup test for '{backupJob.Name}'...");
        success = await httpRunner.RunJobAsync(backupJob, backupOptions, CancellationToken.None);
    }
    else if (backupType == "FTP_UPLOAD")
    {
        Console.WriteLine($"Starting FTP Upload backup test for '{backupJob.Name}'...");
        success = await ftpUploadRunner.RunJobAsync(backupJob, backupOptions, CancellationToken.None);
    }
    else
    {
        Console.WriteLine($"Starting FTP backup test for '{backupJob.Name}'...");
        success = await ftpRunner.RunJobAsync(backupJob, backupOptions, CancellationToken.None);
    }

    if (success)
    {
        Console.WriteLine("Backup completed!");
    }
    else
    {
        Console.WriteLine("Backup failed! Check the logs for more details.");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Backup failed! Reason: {ex.Message}");
}

host.Run();
