namespace ContextMessenger.Core.Roslyn;

public sealed record GetSymbolSourceQuery
{
    public string? SymbolId { get; init; }

    public string? Name { get; init; }

    public string Match { get; init; } = "exact";

    public IReadOnlyList<string> Kinds { get; init; } = [];

    public string? Project { get; init; }

    public bool IncludeNonPublic { get; init; } = true;

    public string? RelativePath { get; init; }

    public int? Line { get; init; }

    public int? Column { get; init; }

    public int MaxLines { get; init; } = 400;

    public long MaxBytes { get; init; } = 1_048_576;
}
