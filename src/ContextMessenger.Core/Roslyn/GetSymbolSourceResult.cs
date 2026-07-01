namespace ContextMessenger.Core.Roslyn;

public sealed class GetSymbolSourceResult
{
    public string WorkspaceVersion { get; init; } = "";

    public SymbolSummary? Symbol { get; init; }

    public SymbolSourceBlock? Source { get; init; }
}
