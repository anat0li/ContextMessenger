using System.Text.Json.Serialization;
using ContextMessenger.Core.Roslyn;
using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.Protocol.Commands;

public sealed class GotoDefinitionCommandParams
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("line")]
    public int Line { get; set; }

    [JsonPropertyName("column")]
    public int Column { get; set; }
}

public sealed class GotoDefinitionCommandResult
{
    [JsonPropertyName("workspaceVersion")]
    public string WorkspaceVersion { get; set; } = "";

    [JsonPropertyName("definitions")]
    public IReadOnlyList<SymbolSummary> Definitions { get; set; } = [];
}

internal sealed class GotoDefinitionHandler : CommandHandlerBase<GotoDefinitionCommandParams, GotoDefinitionCommandResult>
{
    private readonly IRoslynNavigationService _roslyn;

    public GotoDefinitionHandler(IRoslynNavigationService roslyn)
    {
        _roslyn = roslyn ?? throw new ArgumentNullException(nameof(roslyn));
    }

    public override string CommandType => CommandTypes.GotoDefinition;

    protected override GotoDefinitionCommandResult ExecuteCore(GotoDefinitionCommandParams parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters.Path))
            throw new ProtocolException(ProtocolErrorCodes.InvalidParameters, "path is required.");

        var result = _roslyn.GotoDefinition(new GotoDefinitionQuery
        {
            RelativePath = parameters.Path,
            Line = parameters.Line,
            Column = parameters.Column,
        });

        return new GotoDefinitionCommandResult
        {
            WorkspaceVersion = result.WorkspaceVersion,
            Definitions = result.Definitions,
        };
    }
}
