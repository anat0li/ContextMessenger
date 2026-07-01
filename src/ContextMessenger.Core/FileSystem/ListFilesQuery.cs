namespace ContextMessenger.Core.FileSystem;

public sealed record ListFilesQuery
{
    public string RelativePath { get; init; } = ".";
    public IReadOnlyList<string> IncludeGlobs { get; init; } = [];
    public IReadOnlyList<string> ExcludeGlobs { get; init; } = [];
    public int MaxResults { get; init; } = 5000;
}
