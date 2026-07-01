using System.Text.Json.Serialization;
using ContextMessenger.Core.Roslyn;
using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.Protocol.Commands;

public sealed class FindReferencesCommandParams
{
    [JsonPropertyName("symbolId")]
    public string SymbolId { get; set; } = "";

    [JsonPropertyName("includeDefinition")]
    public bool IncludeDefinition { get; set; }

    [JsonPropertyName("kinds")]
    public List<string>? Kinds { get; set; }

    [JsonPropertyName("maxResults")]
    public int MaxResults { get; set; } = 500;
}

internal sealed class FindReferencesHandler : CommandHandlerBase<FindReferencesCommandParams, FindReferencesResult>
{
    private readonly IRoslynNavigationService _roslyn;

    public FindReferencesHandler(IRoslynNavigationService roslyn)
    {
        _roslyn = roslyn ?? throw new ArgumentNullException(nameof(roslyn));
    }

    public override string CommandType => CommandTypes.FindReferences;

    protected override FindReferencesResult ExecuteCore(FindReferencesCommandParams parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters.SymbolId))
            throw new ProtocolException(ProtocolErrorCodes.InvalidParameters, "symbolId is required.");

        return _roslyn.FindReferences(new FindReferencesQuery
        {
            SymbolId = parameters.SymbolId,
            IncludeDefinition = parameters.IncludeDefinition,
            Kinds = parameters.Kinds ?? [],
            MaxResults = parameters.MaxResults,
        });
    }
}
