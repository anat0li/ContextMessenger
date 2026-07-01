namespace ContextMessenger.App.Wpf.Settings;

public sealed record LoggingSettings
{
    public string LogTimestampFormat { get; init; } = "HH:mm:ss.fff";

    public string LogFileTimestampFormat { get; init; } = "yyyy-MM-dd HH:mm:ss";

    public bool ShowLogLevel { get; init; }

    public bool EnableUiLogging { get; init; } = true;

    public bool EnableFileLogging { get; init; } = true;

    public bool EnableDebugOutputLogging { get; init; } = true;

    public long MaxFileBytes { get; init; } = 524_288;

    public int MaxJsonPropertyChars { get; init; } = 512;
}
