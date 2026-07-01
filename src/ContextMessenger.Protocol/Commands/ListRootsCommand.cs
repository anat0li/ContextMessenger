using System.Text.Json.Serialization;
using ContextMessenger.Core.Meta;
using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.Protocol.Commands;

public sealed class ListRootsCommandParams
{
}

public sealed class ListRootsCommandResult
{
    [JsonPropertyName("roots")]
    public IReadOnlyList<RootProfileInfo> Roots { get; set; } = [];
}

internal sealed class ListRootsHandler : CommandHandlerBase<ListRootsCommandParams, ListRootsCommandResult>
{
    private readonly IContextSession _session;

    public ListRootsHandler(IContextSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public override string CommandType => CommandTypes.ListRoots;

    protected override ListRootsCommandResult ExecuteCore(ListRootsCommandParams parameters) =>
        new() { Roots = _session.ListRoots() };
}
