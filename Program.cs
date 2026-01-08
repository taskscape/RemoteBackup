using BackupService;
using Microsoft.Extensions.Logging.EventLog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<BackupOptions>(
    builder.Configuration.GetSection(BackupOptions.SectionName));
builder.Services.Configure<FileLoggerOptions>(
    builder.Configuration.GetSection(FileLoggerOptions.SectionName));

builder.Services.AddSingleton<FtpBackupRunner>();
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

var host = builder.Build();
host.Run();
