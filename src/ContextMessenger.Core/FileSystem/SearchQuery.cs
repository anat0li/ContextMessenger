namespace ContextMessenger.Core.FileSystem;

public sealed record SearchQuery(string Pattern)
{
    public bool IsRegex { get; init; }
    public bool IgnoreCase { get; init; } = true;
    public string RelativePath { get; init; } = ".";
    public IReadOnlyList<string> IncludeGlobs { get; init; } = [];
    public IReadOnlyList<string> ExcludeGlobs { get; init; } = [];
    public int MaxResults { get; init; } = 500;
}
