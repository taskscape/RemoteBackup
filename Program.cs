using BackupService;
using Microsoft.Extensions.Logging.EventLog;

var builder = Host.CreateApplicationBuilder(args);
var backupOptions = builder.Configuration.GetSection("BackupOptions").Get<BackupOptions>();

if (backupOptions == null || !backupOptions.Backups.Any())
{
    Console.WriteLine("No backups found in configuration!");
    return; 
}

builder.Services.Configure<BackupOptions>(
    builder.Configuration.GetSection(BackupOptions.SectionName));
builder.Services.Configure<FileLoggerOptions>(
    builder.Configuration.GetSection(FileLoggerOptions.SectionName));

builder.Services.AddSingleton<FtpBackupRunner>();
builder.Services.AddSingleton<FtpUploadRunner>();
builder.Services.AddSingleton<HttpBackupRunner>();
builder.Services.AddSingleton<EmailNotificationService>();
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

var coordinator = host.Services.GetRequiredService<BackupCoordinator>();

try
{
    Console.WriteLine("Starting initial backup test for all configured jobs...");
    bool allSuccessful = await coordinator.RunBackupsAsync(CancellationToken.None);
    
    if (allSuccessful)
    {
        Console.WriteLine("Initial backup test sequence completed successfully!");
    }
    else
    {
        Console.WriteLine("Initial backup test sequence FAILED! Check the logs above for details.");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"An error occurred during the initial test: {ex.Message}");
}

host.Run();
