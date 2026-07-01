namespace ContextMessenger.Core.Roslyn;

public sealed record FindOverridesQuery
{
    public string? SymbolId { get; init; }

    public string? RelativePath { get; init; }

    public int? Line { get; init; }

    public int? Column { get; init; }

    public bool IncludeAbstract { get; init; } = true;

    public int MaxResults { get; init; } = 100;
}
