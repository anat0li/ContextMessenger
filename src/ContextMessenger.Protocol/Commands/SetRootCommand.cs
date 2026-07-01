using System.Text.Json.Serialization;
using ContextMessenger.Core.Meta;
using ContextMessenger.Core.Patching;
using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.Protocol.Commands;

public sealed class SetRootCommandParams
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

public sealed class SetRootCommandResult
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

internal sealed class SetRootHandler : CommandHandlerBase<SetRootCommandParams, SetRootCommandResult>
{
    private readonly IContextSession _session;
    private readonly IPatchTransactionService? _patches;

    public SetRootHandler(IContextSession session, IPatchTransactionService? patches = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _patches = patches;
    }

    public override string CommandType => CommandTypes.SetRoot;

    protected override SetRootCommandResult ExecuteCore(SetRootCommandParams parameters)
    {
        if (string.IsNullOrEmpty(parameters.Name))
            throw new ProtocolException(ProtocolErrorCodes.InvalidParameters, "Root name is required.");
        if (_patches?.HasActivePatch == true)
            throw new ProtocolException(
                ProtocolErrorCodes.PatchInProgress,
                "Cannot switch roots while a patch is active. Send revert_patch first.");

        var info = _session.SetRoot(parameters.Name);
        return new SetRootCommandResult
        {
            RootProfile = info.RootProfile,
            Target = info.Target,
            Server = info.Server,
            Protocol = info.Protocol,
        };
    }
}
