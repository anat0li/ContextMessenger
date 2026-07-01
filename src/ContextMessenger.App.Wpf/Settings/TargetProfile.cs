using System.Text.Json.Serialization;

namespace ContextMessenger.App.Wpf.Settings;

public sealed record TargetProfile
{
    public string Name { get; init; } = "";

    public string ProcessName { get; init; } = "";

    public string? WindowTitleHint { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    public TargetAutomationSettings Automation { get; init; } = new();

    public IReadOnlyList<TargetRootSettings> Roots { get; init; } = [];
}
