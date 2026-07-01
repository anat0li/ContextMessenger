namespace ContextMessenger.Core.Roslyn;

public sealed record FindCallersQuery
{
    public string SymbolId { get; init; } = "";

    public int MaxResults { get; init; } = 500;
}
