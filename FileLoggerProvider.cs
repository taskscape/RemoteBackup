using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text;
using System.Globalization;

namespace BackupService;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly IOptionsMonitor<FileLoggerOptions> _optionsMonitor;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();

    public FileLoggerProvider(IOptionsMonitor<FileLoggerOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor;
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name =>
            new FileLogger(name, _optionsMonitor));

    public void Dispose()
    {
        _loggers.Clear();
    }

    private sealed class FileLogger : ILogger
    {
        private static readonly object WriteLock = new();
        private static readonly ConcurrentDictionary<string, DateOnly> LastCleanupByPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _categoryName;
        private readonly IOptionsMonitor<FileLoggerOptions> _optionsMonitor;

        public FileLogger(
            string categoryName,
            IOptionsMonitor<FileLoggerOptions> optionsMonitor)
        {
            _categoryName = categoryName;
            _optionsMonitor = optionsMonitor;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull =>
            NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel >= _optionsMonitor.CurrentValue.MinimumLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var options = _optionsMonitor.CurrentValue;
            var message = formatter(state, exception);
            var line = BuildLine(logLevel, message, exception);

            var path = GetCurrentLogFilePath(options.Path, DateTime.Now);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            lock (WriteLock)
            {
                CleanupExpiredLogs(options, DateTime.Now);
                File.AppendAllText(path, line, Encoding.UTF8);
            }
        }

        private static string GetCurrentLogFilePath(string configuredPath, DateTime now)
        {
            var directory = Path.GetDirectoryName(configuredPath) ?? string.Empty;
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(configuredPath);
            var extension = Path.GetExtension(configuredPath);
            var dailyFileName = $"{fileNameWithoutExtension}-{now:yyyy-MM-dd}{extension}";

            return string.IsNullOrWhiteSpace(directory)
                ? dailyFileName
                : Path.Combine(directory, dailyFileName);
        }

        private static void CleanupExpiredLogs(FileLoggerOptions options, DateTime now)
        {
            if (options.RetentionDays <= 0)
            {
                return;
            }

            var configuredPath = options.Path;
            var today = DateOnly.FromDateTime(now);
            if (LastCleanupByPath.TryGetValue(configuredPath, out var lastCleanup) && lastCleanup == today)
            {
                return;
            }

            var directory = Path.GetDirectoryName(configuredPath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                LastCleanupByPath[configuredPath] = today;
                return;
            }

            var baseName = Path.GetFileNameWithoutExtension(configuredPath);
            var extension = Path.GetExtension(configuredPath);
            var cutoffDate = today.AddDays(-(options.RetentionDays - 1));

            foreach (var filePath in Directory.EnumerateFiles(directory, $"{baseName}-*{extension}"))
            {
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                if (!TryGetLogDate(fileName, baseName, out var logDate))
                {
                    continue;
                }

                if (logDate < cutoffDate)
                {
                    File.Delete(filePath);
                }
            }

            // Cleanup a legacy single-file log after it ages out of the retention window.
            if (File.Exists(configuredPath))
            {
                var legacyLastWrite = DateOnly.FromDateTime(File.GetLastWriteTime(configuredPath));
                if (legacyLastWrite < cutoffDate)
                {
                    File.Delete(configuredPath);
                }
            }

            LastCleanupByPath[configuredPath] = today;
        }

        private static bool TryGetLogDate(string fileNameWithoutExtension, string baseName, out DateOnly logDate)
        {
            logDate = default;
            var prefix = $"{baseName}-";
            if (!fileNameWithoutExtension.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var datePart = fileNameWithoutExtension[prefix.Length..];
            return DateOnly.TryParseExact(
                datePart,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out logDate);
        }

        private string BuildLine(
            LogLevel level,
            string message,
            Exception? exception)
        {
            var builder = new StringBuilder();
            builder.Append(DateTimeOffset.Now.ToString("O"));
            builder.Append(" [");
            builder.Append(level);
            builder.Append("] ");
            builder.Append(_categoryName);
            builder.Append(" - ");
            builder.Append(message);
            if (exception != null)
            {
                builder.Append(" | ");
                builder.Append(exception);
            }
            builder.AppendLine();
            return builder.ToString();
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
