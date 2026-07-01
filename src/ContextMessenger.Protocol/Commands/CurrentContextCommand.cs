using System.Text.Json.Serialization;
using ContextMessenger.Core.Meta;
using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.Protocol.Commands;

public sealed class CurrentContextCommandParams
{
}

public sealed class CurrentContextCommandResult
{
    [JsonPropertyName("rootProfile")]
    public RootProfileInfo RootProfile { get; set; } = new();

    [JsonPropertyName("target")]
    public TargetProfileInfo Target { get; set; } = new();

    [JsonPropertyName("server")]
    public ServerInfo Server { get; set; } = new();

    [JsonPropertyName("protocol")]
    public ProtocolInfo Protocol { get; set; } = new();
}

internal sealed class CurrentContextHandler : CommandHandlerBase<CurrentContextCommandParams, CurrentContextCommandResult>
{
    private readonly IContextSession _session;

    public CurrentContextHandler(IContextSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public override string CommandType => CommandTypes.CurrentContext;

    protected override CurrentContextCommandResult ExecuteCore(CurrentContextCommandParams parameters)
    {
        var info = _session.GetCurrentContext();
        return new CurrentContextCommandResult
        {
            RootProfile = info.RootProfile,
            Target = info.Target,
            Server = info.Server,
            Protocol = info.Protocol,
        };
    }
}
