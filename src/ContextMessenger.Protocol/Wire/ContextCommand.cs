using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContextMessenger.Protocol.Wire;

public sealed class ContextCommand
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Parameters { get; set; } = new();
}
