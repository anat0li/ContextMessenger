using System.Text.Json.Serialization;
using ContextMessenger.Core.Meta;
using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.Protocol.Commands;

public sealed class ListTargetsCommandParams
{
}

public sealed class ListTargetsCommandResult
{
    [JsonPropertyName("targets")]
    public IReadOnlyList<TargetProfileInfo> Targets { get; set; } = [];
}

internal sealed class ListTargetsHandler : CommandHandlerBase<ListTargetsCommandParams, ListTargetsCommandResult>
{
    private readonly IContextSession _session;

    public ListTargetsHandler(IContextSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public override string CommandType => CommandTypes.ListTargets;

    protected override ListTargetsCommandResult ExecuteCore(ListTargetsCommandParams parameters) =>
        new() { Targets = _session.ListTargets() };
}
