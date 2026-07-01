using System.Text.Json.Serialization;
using ContextMessenger.Core.Roslyn;
using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.Protocol.Commands;

public sealed class FindDerivedTypesCommandParams
{
    [JsonPropertyName("symbolId")]
    public string SymbolId { get; set; } = "";

    [JsonPropertyName("transitive")]
    public bool Transitive { get; set; }

    [JsonPropertyName("includeAbstract")]
    public bool IncludeAbstract { get; set; } = true;

    [JsonPropertyName("maxResults")]
    public int MaxResults { get; set; } = 100;
}

internal sealed class FindDerivedTypesHandler : CommandHandlerBase<FindDerivedTypesCommandParams, FindDerivedTypesResult>
{
    private readonly IRoslynNavigationService _roslyn;

    public FindDerivedTypesHandler(IRoslynNavigationService roslyn)
    {
        _roslyn = roslyn ?? throw new ArgumentNullException(nameof(roslyn));
    }

    public override string CommandType => CommandTypes.FindDerivedTypes;

    protected override FindDerivedTypesResult ExecuteCore(FindDerivedTypesCommandParams parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters.SymbolId))
            throw new ProtocolException(ProtocolErrorCodes.InvalidParameters, "symbolId is required.");

        return _roslyn.FindDerivedTypes(new FindDerivedTypesQuery
        {
            SymbolId = parameters.SymbolId,
            Transitive = parameters.Transitive,
            IncludeAbstract = parameters.IncludeAbstract,
            MaxResults = parameters.MaxResults,
        });
    }
}
