using System.Text.Json.Serialization;

namespace ContextMessenger.Core.Meta;

public sealed class CurrentContextInfo
{
    [JsonPropertyName("rootProfile")]
    public RootProfileInfo RootProfile { get; init; } = new();

    [JsonPropertyName("target")]
    public TargetProfileInfo Target { get; init; } = new();

    [JsonPropertyName("server")]
    public ServerInfo Server { get; init; } = new();

    [JsonPropertyName("protocol")]
    public ProtocolInfo Protocol { get; init; } = new();
}
