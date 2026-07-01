namespace ContextMessenger.Core.FileSystem;

public sealed record TreeQuery(string RelativePath = ".")
{
    public int MaxDepth { get; init; } = 3;
    public IReadOnlyList<string> IncludeGlobs { get; init; } = [];
    public IReadOnlyList<string> ExcludeGlobs { get; init; } = [];
}
