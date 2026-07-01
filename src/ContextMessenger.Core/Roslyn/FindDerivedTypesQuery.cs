namespace ContextMessenger.Core.Roslyn;

public sealed record FindDerivedTypesQuery
{
    public string SymbolId { get; init; } = "";

    public bool Transitive { get; init; }

    public bool IncludeAbstract { get; init; } = true;

    public int MaxResults { get; init; } = 100;
}
