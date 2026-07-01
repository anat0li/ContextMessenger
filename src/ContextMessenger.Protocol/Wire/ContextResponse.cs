using System.Text.Json.Serialization;

namespace ContextMessenger.Protocol.Wire;

public sealed class ContextResponse
{
    [JsonPropertyName("version")]
    public Version Version { get; set; } = ProtocolValidator.CurrentVersion;

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = ProtocolStatus.Ok;

    [JsonPropertyName("serverTimeUtc")]
    public string ServerTimeUtc { get; set; } = "";

    [JsonPropertyName("results")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ContextResponseResult>? Results { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContextResponseError? Error { get; set; }
}
