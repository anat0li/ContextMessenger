using System.Text.Json.Serialization;

namespace ContextMessenger.Protocol.Wire;

public sealed class ContextRequest
{
    [JsonPropertyName("version")]
    public Version Version { get; set; } = ProtocolValidator.CurrentVersion;

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("commands")]
    public List<ContextCommand> Commands { get; set; } = new();
}
