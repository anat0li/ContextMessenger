using System.Text.Json.Serialization;

namespace ContextMessenger.Core.Meta;

public sealed class CommandFeatureInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("values")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Values { get; init; }

    [JsonPropertyName("kinds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CommandEditKindInfo>? Kinds { get; init; }
}

public sealed class CommandEditKindInfo
{
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "";

    [JsonPropertyName("required")]
    public IReadOnlyList<string> Required { get; init; } = [];

    [JsonPropertyName("optional")]
    public IReadOnlyList<string> Optional { get; init; } = [];

    [JsonPropertyName("expectedAnchorHashTarget")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExpectedAnchorHashTarget { get; init; }
}
