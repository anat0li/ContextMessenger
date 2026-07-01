using System.Text.Json.Serialization;
using ContextMessenger.Core.Roslyn;
using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.Protocol.Commands;

public sealed class FindSymbolCommandParams
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("match")]
    public string Match { get; set; } = "exact";

    [JsonPropertyName("kinds")]
    public List<string>? Kinds { get; set; }

    [JsonPropertyName("project")]
    public string? Project { get; set; }

    [JsonPropertyName("includeNonPublic")]
    public bool IncludeNonPublic { get; set; }

    [JsonPropertyName("ignoreCase")]
    public bool IgnoreCase { get; set; } = true;

    [JsonPropertyName("maxResults")]
    public int MaxResults { get; set; } = 100;
}

public sealed class FindSymbolCommandResult
{
    [JsonPropertyName("workspaceVersion")]
    public string WorkspaceVersion { get; set; } = "";

    [JsonPropertyName("matches")]
    public IReadOnlyList<SymbolSummary> Matches { get; set; } = [];
}

internal sealed class FindSymbolHandler : CommandHandlerBase<FindSymbolCommandParams, FindSymbolCommandResult>
{
    private readonly IRoslynNavigationService _roslyn;

    public FindSymbolHandler(IRoslynNavigationService roslyn)
    {
        _roslyn = roslyn ?? throw new ArgumentNullException(nameof(roslyn));
    }

    public override string CommandType => CommandTypes.FindSymbol;

    protected override FindSymbolCommandResult ExecuteCore(FindSymbolCommandParams parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters.Name))
            throw new ProtocolException(ProtocolErrorCodes.InvalidParameters, "name is required.");

        var result = _roslyn.FindSymbols(new FindSymbolQuery
        {
            Name = parameters.Name,
            Match = string.IsNullOrWhiteSpace(parameters.Match) ? "exact" : parameters.Match,
            Kinds = parameters.Kinds ?? [],
            Project = parameters.Project,
            IncludeNonPublic = parameters.IncludeNonPublic,
            IgnoreCase = parameters.IgnoreCase,
            MaxResults = parameters.MaxResults,
        });

        return new FindSymbolCommandResult
        {
            WorkspaceVersion = result.WorkspaceVersion,
            Matches = result.Matches,
        };
    }
}
