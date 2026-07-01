using ContextMessenger.Core.FileSystem;
using Microsoft.Extensions.FileSystemGlobbing;

namespace ContextMessenger.FileSystem;

public sealed class RepoTreeProvider
{
    private readonly PathSandbox _sandbox;

    public RepoTreeProvider(PathSandbox sandbox)
    {
        _sandbox = sandbox ?? throw new ArgumentNullException(nameof(sandbox));
    }

    public TreeNode GetTree(TreeQuery query)
    {
        if (query.MaxDepth < 0)
            throw new ArgumentOutOfRangeException(nameof(query), "MaxDepth must be non-negative.");

        var rootAbs = _sandbox.ResolveAbsolute(query.RelativePath);

        if (File.Exists(rootAbs))
        {
            return new TreeNode(
                Name: Path.GetFileName(rootAbs),
                RelativePath: _sandbox.ToRelative(rootAbs),
                IsDirectory: false,
                Children: []);
        }

        if (!Directory.Exists(rootAbs))
            throw new DirectoryNotFoundException($"Path does not exist: {query.RelativePath}");

        var matcher = GlobMatcherFactory.Build(query.IncludeGlobs, query.ExcludeGlobs);
        var excludedDirectoryNames = GlobMatcherFactory.GetDirectoryExcludeNames(query.ExcludeGlobs);
        return BuildNode(
            rootAbs,
            rootAbs,
            query.MaxDepth,
            matcher,
            query.IncludeGlobs,
            query.ExcludeGlobs,
            excludedDirectoryNames);
    }

    private TreeNode BuildNode(string rootAbs, string dirAbs, int remainingDepth, Matcher fileMatcher,
        IReadOnlyList<string> includeGlobs,
        IReadOnlyList<string> excludeGlobs,
        IReadOnlySet<string> excludedDirectoryNames)
    {
        var name = string.Equals(dirAbs, _sandbox.Root, StringComparison.OrdinalIgnoreCase)
            ? "."
            : Path.GetFileName(dirAbs);
        var relPath = _sandbox.ToRelative(dirAbs);

        if (remainingDepth == 0)
            return new TreeNode(name, relPath, IsDirectory: true, Children: []);

        var children = new List<TreeNode>();

        IEnumerable<string> subDirs;
        IEnumerable<string> files;
        try
        {
            subDirs = Directory.EnumerateDirectories(dirAbs)
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase);
            files = Directory.EnumerateFiles(dirAbs)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
        }
        catch (UnauthorizedAccessException)
        {
            return new TreeNode(name, relPath, IsDirectory: true, Children: []);
        }

        var filterOff = includeGlobs.Count == 0;
        foreach (var subDir in subDirs)
        {
            var subName = Path.GetFileName(subDir);
            var included = includeGlobs.Contains(subName, StringComparer.OrdinalIgnoreCase);
            if (!included && (DefaultExclusions.IsExcludedDirectoryName(subName)
                              || excludedDirectoryNames.Contains(subName)
                              || excludeGlobs.Contains(subName, StringComparer.OrdinalIgnoreCase)))
                continue;
            var child = BuildNode(
                rootAbs,
                subDir,
                remainingDepth - 1,
                fileMatcher,
                includeGlobs,
                excludeGlobs,
                excludedDirectoryNames);
            if (filterOff || child.Children.Count > 0 || included)
                children.Add(child);
        }

        foreach (var file in files)
        {
            var fileRel = _sandbox.ToRelative(file);
            var matchRel = ToRelativePath(rootAbs, file);
            if (!fileMatcher.Match(rootAbs, matchRel).HasMatches) continue;

            children.Add(new TreeNode(
                Name: Path.GetFileName(file),
                RelativePath: fileRel,
                IsDirectory: false,
                Children: []));
        }

        return new TreeNode(name, relPath, IsDirectory: true, children);
    }

    private static string ToRelativePath(string rootAbs, string fileAbs) =>
        Path.GetRelativePath(rootAbs, fileAbs).Replace(Path.DirectorySeparatorChar, '/');

}
