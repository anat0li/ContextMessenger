using System.Text.Json.Serialization;
using ContextMessenger.Core.Roslyn;
using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.Protocol.Commands;

public sealed class DocumentSymbolsCommandParams
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("includeNonPublic")]
    public bool IncludeNonPublic { get; set; } = true;
}

internal sealed class DocumentSymbolsHandler : CommandHandlerBase<DocumentSymbolsCommandParams, DocumentSymbolsResult>
{
    private readonly IRoslynNavigationService _roslyn;

    public DocumentSymbolsHandler(IRoslynNavigationService roslyn)
    {
        _roslyn = roslyn ?? throw new ArgumentNullException(nameof(roslyn));
    }

    public override string CommandType => CommandTypes.DocumentSymbols;

    protected override DocumentSymbolsResult ExecuteCore(DocumentSymbolsCommandParams parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters.Path))
            throw new ProtocolException(ProtocolErrorCodes.InvalidParameters, "path is required.");

        return _roslyn.GetDocumentSymbols(new DocumentSymbolsQuery
        {
            RelativePath = parameters.Path,
            IncludeNonPublic = parameters.IncludeNonPublic,
        });
    }
}
