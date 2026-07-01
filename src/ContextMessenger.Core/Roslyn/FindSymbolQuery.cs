namespace ContextMessenger.Core.Roslyn;

public sealed record FindSymbolQuery
{
    public string Name { get; init; } = "";

    public string Match { get; init; } = "exact";

    public IReadOnlyList<string> Kinds { get; init; } = [];

    public string? Project { get; init; }

    public bool IncludeNonPublic { get; init; }

    public bool IgnoreCase { get; init; } = true;

    public int MaxResults { get; init; } = 100;
}
