namespace ContextMessenger.Core.Roslyn;

public sealed record FindImplementationsQuery
{
    public string SymbolId { get; init; } = "";

    public bool Transitive { get; init; }

    public bool IncludeAbstract { get; init; }

    public int MaxResults { get; init; } = 100;
}
