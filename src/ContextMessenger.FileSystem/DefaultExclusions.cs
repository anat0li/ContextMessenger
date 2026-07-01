namespace ContextMessenger.FileSystem;

public static class DefaultExclusions
{
    public static IReadOnlyList<string> DirectoryNames { get; } =
    [
        ".git",
        ".vs",
        ".idea",
        "bin",
        "obj",
        "packages",
        "TestResults",
        "node_modules",
    ];

    private static readonly HashSet<string> DirectoryNameSet =
        new(DirectoryNames, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Globs { get; } =
        DirectoryNames.Select(n => $"**/{n}/**").ToArray();

    public static bool IsExcludedDirectoryName(string directoryName) =>
        directoryName.StartsWith(".", StringComparison.Ordinal)
        || DirectoryNameSet.Contains(directoryName);
}
