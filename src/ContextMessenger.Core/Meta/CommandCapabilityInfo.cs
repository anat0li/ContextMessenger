using System.Text.Json.Serialization;

namespace ContextMessenger.Core.Meta;

public sealed class CommandCapabilityInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("category")]
    public string Category { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("sideEffects")]
    public string SideEffects { get; init; } = "none";

    [JsonPropertyName("parameters")]
    public IReadOnlyList<CommandParameterInfo> Parameters { get; init; } = [];

    [JsonPropertyName("features")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CommandFeatureInfo>? Features { get; init; }
}
