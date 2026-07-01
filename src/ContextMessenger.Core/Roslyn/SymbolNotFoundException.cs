namespace ContextMessenger.Core.Roslyn;

public sealed class SymbolNotFoundException : Exception
{
    public string SymbolId { get; }

    public SymbolNotFoundException(string symbolId)
        : base($"Symbol not found: {symbolId}")
    {
        SymbolId = symbolId;
    }
}
