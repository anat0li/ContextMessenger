using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using ContextMessenger.Core.Roslyn;
using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.Protocol.Commands;

public sealed class GetSymbolSourceCommandParams
{
    [JsonPropertyName("symbolId")]
    public string? SymbolId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("match")]
    public string Match { get; set; } = "exact";

    [JsonPropertyName("kinds")]
    public IReadOnlyList<string> Kinds { get; set; } = [];

    [JsonPropertyName("project")]
    public string? Project { get; set; }

    [JsonPropertyName("includeNonPublic")]
    public bool IncludeNonPublic { get; set; } = true;

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("line")]
    public int? Line { get; set; }

    [JsonPropertyName("column")]
    public int? Column { get; set; }

    [JsonPropertyName("maxLines")]
    public int MaxLines { get; set; } = 400;

    [JsonPropertyName("maxBytes")]
    public long MaxBytes { get; set; } = 1_048_576;
}

public sealed class GetSymbolSourceCommandResult
{
    [JsonPropertyName("workspaceVersion")]
    public string WorkspaceVersion { get; set; } = "";

    [JsonPropertyName("symbol")]
    public SymbolSummary Symbol { get; set; } = new();

    [JsonPropertyName("source")]
    public SymbolSourceBlock Source { get; set; } = new();
}

internal sealed class GetSymbolSourceHandler : CommandHandlerBase<GetSymbolSourceCommandParams, GetSymbolSourceCommandResult>
{
    private readonly IRoslynNavigationService _roslyn;

    public GetSymbolSourceHandler(IRoslynNavigationService roslyn)
    {
        _roslyn = roslyn ?? throw new ArgumentNullException(nameof(roslyn));
    }

    public override string CommandType => CommandTypes.GetSymbolSource;

    protected override GetSymbolSourceCommandResult ExecuteCore(GetSymbolSourceCommandParams parameters)
    {
        var hasSymbolId = !string.IsNullOrWhiteSpace(parameters.SymbolId);
        var hasName = !string.IsNullOrWhiteSpace(parameters.Name);
        var hasLocation = !string.IsNullOrWhiteSpace(parameters.Path) ||
                          parameters.Line is not null ||
                          parameters.Column is not null;
        var selectorCount = (hasSymbolId ? 1 : 0) + (hasName ? 1 : 0) + (hasLocation ? 1 : 0);
        if (selectorCount == 0)
            throw new ProtocolException(ProtocolErrorCodes.InvalidParameters, "symbolId, name, or path/line/column is required.");
        if (selectorCount > 1)
            throw new ProtocolException(ProtocolErrorCodes.InvalidParameters, "Provide exactly one selector: symbolId, name, or path/line/column.");
        if (hasLocation && (string.IsNullOrWhiteSpace(parameters.Path) || parameters.Line is null || parameters.Column is null))
            throw new ProtocolException(ProtocolErrorCodes.InvalidParameters, "path, line, and column are required together.");
        if (parameters.MaxLines <= 0)
            throw new ProtocolException(ProtocolErrorCodes.InvalidParameters, "maxLines must be positive.");
        if (parameters.MaxBytes <= 0)
            throw new ProtocolException(ProtocolErrorCodes.InvalidParameters, "maxBytes must be positive.");

        var result = _roslyn.GetSymbolSource(new GetSymbolSourceQuery
        {
            SymbolId = parameters.SymbolId,
            Name = parameters.Name,
            Match = parameters.Match,
            Kinds = parameters.Kinds,
            Project = parameters.Project,
            IncludeNonPublic = parameters.IncludeNonPublic,
            RelativePath = parameters.Path,
            Line = parameters.Line,
            Column = parameters.Column,
            MaxLines = parameters.MaxLines,
            MaxBytes = parameters.MaxBytes,
        });

        var source = result.Source ?? throw new ProtocolException(ProtocolErrorCodes.InvalidParameters, "Resolved symbol does not have source text.");
        var sourceHash = HashText(source.Text);

        return new GetSymbolSourceCommandResult
        {
            WorkspaceVersion = result.WorkspaceVersion,
            Symbol = result.Symbol ?? throw new SymbolNotFoundException(parameters.SymbolId ?? parameters.Name ?? $"{parameters.Path}:{parameters.Line}:{parameters.Column}"),
            Source = new SymbolSourceBlock
            {
                Path = source.Path,
                StartLine = source.StartLine,
                StartColumn = source.StartColumn,
                EndLine = source.EndLine,
                EndColumn = source.EndColumn,
                Language = source.Language,
                Text = source.Text,
                Hash = sourceHash,
                OldSourceHash = sourceHash,
            },
        };
    }

    private static string HashText(string text) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
