using System.Text.Json.Serialization;

namespace ContextMessenger.App.Wpf.Settings;

public sealed record TargetRootSettings
{
    public string RootName { get; init; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AutoProcessEnabled { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Order { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsActive { get; init; }
}
