using System.Text.Json.Serialization;
using ContextMessenger.Core.Roslyn;
using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.Protocol.Commands;

public sealed class FindCallersCommandParams
{
    [JsonPropertyName("symbolId")]
    public string SymbolId { get; set; } = "";

    [JsonPropertyName("maxResults")]
    public int MaxResults { get; set; } = 500;
}

internal sealed class FindCallersHandler : CommandHandlerBase<FindCallersCommandParams, FindCallersResult>
{
    private readonly IRoslynNavigationService _roslyn;

    public FindCallersHandler(IRoslynNavigationService roslyn)
    {
        _roslyn = roslyn ?? throw new ArgumentNullException(nameof(roslyn));
    }

    public override string CommandType => CommandTypes.FindCallers;

    protected override FindCallersResult ExecuteCore(FindCallersCommandParams parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters.SymbolId))
            throw new ProtocolException(ProtocolErrorCodes.InvalidParameters, "symbolId is required.");

        return _roslyn.FindCallers(new FindCallersQuery
        {
            SymbolId = parameters.SymbolId,
            MaxResults = parameters.MaxResults,
        });
    }
}
