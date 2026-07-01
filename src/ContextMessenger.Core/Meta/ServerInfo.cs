using System.Text.Json.Serialization;

namespace ContextMessenger.Core.Meta;

public sealed class ServerInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "ContextMessenger";

    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    [JsonPropertyName("build")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Build { get; init; }
}
