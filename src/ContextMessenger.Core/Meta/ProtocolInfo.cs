using System.Text.Json.Serialization;

namespace ContextMessenger.Core.Meta;

public sealed class ProtocolInfo
{
    [JsonPropertyName("supported")]
    public IReadOnlyList<string> Supported { get; init; } = ["1.0"];
}
