using System.Text.Json.Serialization;
using ContextMessenger.Core.Patching;

namespace ContextMessenger.App.Wpf.Settings;

public sealed record AppSettings
{
    public IReadOnlyList<TargetProfile> Targets { get; init; } = [];

    public IReadOnlyList<RootProfile> Roots { get; init; } = [];

    public string? CurrentTargetName { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CurrentRootName { get; init; }

    public LoggingSettings Logging { get; init; } = new();

    public int LargePayloadThresholdBytes { get; init; } = 32_768;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PatchSessionMetadata? ActivePatch { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AutoProcessEnabled { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LogTimestampFormat { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LogFileTimestampFormat { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ShowLogLevel { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? EnableFileLogging { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? EnableDebugOutputLogging { get; init; }

    /// <summary>
    /// Legacy single-root field. Read on first load to migrate into <see cref="Roots"/>.
    /// Phase A leaves it in place; Phase C drops it from new writes once
    /// MainViewModel switches to selecting a <see cref="RootProfile"/>.
    /// </summary>
    public string? LastRoot { get; init; }
}
