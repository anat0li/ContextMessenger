using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContextMessenger.Protocol.Wire;

public sealed class ContextResponseResult
{
    [JsonPropertyName("commandIndex")]
    public int CommandIndex { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = ProtocolStatus.Ok;

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContextResponseError? Error { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Payload { get; set; } = new();
}
