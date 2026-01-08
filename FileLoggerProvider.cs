using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text;

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

            var path = options.Path;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            lock (WriteLock)
            {
                File.AppendAllText(path, line, Encoding.UTF8);
            }
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
