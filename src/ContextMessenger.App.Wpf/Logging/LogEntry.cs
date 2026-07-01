using Microsoft.Extensions.Logging;

namespace ContextMessenger.App.Wpf.Logging;

public sealed class LogEntry
{
    public DateTimeOffset Timestamp { get; init; }

    public LogLevel Level { get; init; }

    public LogEntryKind Kind { get; init; }

    public string Message { get; init; } = "";

    public int RepeatCount { get; init; } = 1;
}
