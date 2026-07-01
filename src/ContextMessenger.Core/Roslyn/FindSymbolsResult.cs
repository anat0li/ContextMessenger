namespace ContextMessenger.Core.Roslyn;

public sealed class FindSymbolsResult
{
    public string WorkspaceVersion { get; init; } = "";

    public IReadOnlyList<SymbolSummary> Matches { get; init; } = [];
}
