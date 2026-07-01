using System.Text.Json.Serialization;

namespace ContextMessenger.Core.Meta;

public sealed class TargetProfileInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("process")]
    public string Process { get; init; } = "";

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("isCurrent")]
    public bool IsCurrent { get; init; }
}
