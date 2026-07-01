using System.Text.Json.Serialization;

namespace ContextMessenger.Protocol.Wire;

public sealed class ContextResponseEnvelope
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("encoding")]
    public string Encoding { get; set; } = "gzip+base64";

    [JsonPropertyName("payload")]
    public string Payload { get; set; } = "";
}
