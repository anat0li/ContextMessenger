using System.Text.Json.Serialization;

namespace ContextMessenger.Core.Meta;

public sealed class RootProfileInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("kind")]
    [JsonConverter(typeof(JsonStringEnumConverter<RootKind>))]
    public RootKind Kind { get; init; }

    [JsonPropertyName("readOnly")]
    public bool ReadOnly { get; init; }

    [JsonPropertyName("isCurrent")]
    public bool IsCurrent { get; init; }
}
