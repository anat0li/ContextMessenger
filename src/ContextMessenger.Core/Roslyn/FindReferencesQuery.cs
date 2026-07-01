namespace ContextMessenger.Core.Roslyn;

public sealed record FindReferencesQuery
{
    public string SymbolId { get; init; } = "";

    public bool IncludeDefinition { get; init; }

    public IReadOnlyList<string> Kinds { get; init; } = [];

    public int MaxResults { get; init; } = 500;
}
