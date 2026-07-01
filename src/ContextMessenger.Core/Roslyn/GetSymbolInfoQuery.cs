namespace ContextMessenger.Core.Roslyn;

public sealed record GetSymbolInfoQuery
{
    public string SymbolId { get; init; } = "";
}
