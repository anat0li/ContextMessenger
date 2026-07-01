using Microsoft.Extensions.Logging;

namespace ContextMessenger.App.Wpf.Logging;

public sealed class UiLogProvider : ILoggerProvider
{
    private readonly Action<LogEntry> _append;

    public UiLogProvider(Action<LogEntry> append)
    {
        _append = append ?? throw new ArgumentNullException(nameof(append));
    }

    public ILogger CreateLogger(string categoryName) => new UiLogger(_append);

    public void Dispose()
    {
    }

    private sealed class UiLogger : ILogger
    {
        private readonly Action<LogEntry> _append;

        public UiLogger(Action<LogEntry> append)
        {
            _append = append;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception);
            if (exception is not null)
                message = string.IsNullOrWhiteSpace(message) ? exception.ToString() : $"{message}{Environment.NewLine}{exception}";

            _append(new LogEntry
            {
                Timestamp = DateTimeOffset.Now,
                Level = logLevel,
                Kind = ToKind(logLevel),
                Message = message,
            });
        }

        private static LogEntryKind ToKind(LogLevel level) => level switch
        {
            LogLevel.Warning => LogEntryKind.Warning,
            LogLevel.Error or LogLevel.Critical => LogEntryKind.Error,
            _ => LogEntryKind.Info,
        };
    }
}
