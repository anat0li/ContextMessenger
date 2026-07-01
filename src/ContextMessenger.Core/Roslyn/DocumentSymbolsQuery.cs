namespace ContextMessenger.Core.Roslyn;

public sealed record DocumentSymbolsQuery
{
    public string RelativePath { get; init; } = "";

    public bool IncludeNonPublic { get; init; } = true;
}
