using System.Text.Json.Serialization;
using ContextMessenger.Core.Roslyn;
using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.Protocol.Commands;

public sealed class FindImplementationsCommandParams
{
    [JsonPropertyName("symbolId")]
    public string SymbolId { get; set; } = "";

    [JsonPropertyName("transitive")]
    public bool Transitive { get; set; }

    [JsonPropertyName("includeAbstract")]
    public bool IncludeAbstract { get; set; }

    [JsonPropertyName("maxResults")]
    public int MaxResults { get; set; } = 100;
}

internal sealed class FindImplementationsHandler : CommandHandlerBase<FindImplementationsCommandParams, FindImplementationsResult>
{
    private readonly IRoslynNavigationService _roslyn;

    public FindImplementationsHandler(IRoslynNavigationService roslyn)
    {
        _roslyn = roslyn ?? throw new ArgumentNullException(nameof(roslyn));
    }

    public override string CommandType => CommandTypes.FindImplementations;

    protected override FindImplementationsResult ExecuteCore(FindImplementationsCommandParams parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters.SymbolId))
            throw new ProtocolException(ProtocolErrorCodes.InvalidParameters, "symbolId is required.");

        return _roslyn.FindImplementations(new FindImplementationsQuery
        {
            SymbolId = parameters.SymbolId,
            Transitive = parameters.Transitive,
            IncludeAbstract = parameters.IncludeAbstract,
            MaxResults = parameters.MaxResults,
        });
    }
}
