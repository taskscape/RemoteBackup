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
var httpRunner = host.Services.GetRequiredService<HttpBackupRunner>();

try
{
    if (backupJob.BackupType?.ToUpper() == "HTTP")
    {
        Console.WriteLine($"Starting HTTP backup test for '{backupJob.Name}'...");
        await httpRunner.RunJobAsync(backupJob, backupOptions, CancellationToken.None);
    }
    else
    {
        Console.WriteLine($"Starting FTP backup test for '{backupJob.Name}'...");
        await ftpRunner.RunJobAsync(backupJob, backupOptions, CancellationToken.None);
    }
    Console.WriteLine("Backup test completed!");
}
catch (Exception ex)
{
    Console.WriteLine($"An error occurred during the test: {ex.Message}");
}

host.Run();
