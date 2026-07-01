using System.Text.Json.Serialization;
using ContextMessenger.Core.Roslyn;
using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.Protocol.Commands;

public sealed class FindOverridesCommandParams
{
    [JsonPropertyName("symbolId")]
    public string? SymbolId { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("line")]
    public int? Line { get; set; }

    [JsonPropertyName("column")]
    public int? Column { get; set; }

    [JsonPropertyName("includeAbstract")]
    public bool IncludeAbstract { get; set; } = true;

    [JsonPropertyName("maxResults")]
    public int MaxResults { get; set; } = 100;
}

internal sealed class FindOverridesHandler : CommandHandlerBase<FindOverridesCommandParams, FindOverridesResult>
{
    private readonly IRoslynNavigationService _roslyn;

    public FindOverridesHandler(IRoslynNavigationService roslyn)
    {
        _roslyn = roslyn ?? throw new ArgumentNullException(nameof(roslyn));
    }

    public override string CommandType => CommandTypes.FindOverrides;

    protected override FindOverridesResult ExecuteCore(FindOverridesCommandParams parameters)
    {
        var hasSymbolId = !string.IsNullOrWhiteSpace(parameters.SymbolId);
        var hasLocation = !string.IsNullOrWhiteSpace(parameters.Path) ||
                          parameters.Line is not null ||
                          parameters.Column is not null;
        if (!hasSymbolId && !hasLocation)
            throw new ProtocolException(ProtocolErrorCodes.InvalidParameters, "symbolId or path/line/column is required.");
        if (hasLocation && (string.IsNullOrWhiteSpace(parameters.Path) || parameters.Line is null || parameters.Column is null))
            throw new ProtocolException(ProtocolErrorCodes.InvalidParameters, "path, line, and column are required together.");

        return _roslyn.FindOverrides(new FindOverridesQuery
        {
            SymbolId = parameters.SymbolId,
            RelativePath = parameters.Path,
            Line = parameters.Line,
            Column = parameters.Column,
            IncludeAbstract = parameters.IncludeAbstract,
            MaxResults = parameters.MaxResults,
        });
    }
}
