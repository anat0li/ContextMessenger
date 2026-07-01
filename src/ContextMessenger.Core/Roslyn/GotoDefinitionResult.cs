namespace ContextMessenger.Core.Roslyn;

public sealed class GotoDefinitionResult
{
    public string WorkspaceVersion { get; init; } = "";

    public IReadOnlyList<SymbolSummary> Definitions { get; init; } = [];
}
