using ContextMessenger.Core.Patching;
using LibGit2Sharp;

namespace ContextMessenger.Patching;

/// <summary>
/// Computes the unified diff of a single file between HEAD and the working tree using
/// LibGit2Sharp, for the review page. The applied (unstaged) patch lives in the working tree,
/// so a HEAD → working-directory comparison reflects exactly what the reviewer is looking at.
/// </summary>
public sealed class LibGit2SharpPatchDiffService : IPatchDiffService
{
    // Emit the whole file as context so the review page can show (and fold) every unchanged line.
    private const int FullFileContext = 1_000_000;

    private readonly string _rootPath;

    public LibGit2SharpPatchDiffService(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Root path is required.", nameof(rootPath));

        _rootPath = Path.GetFullPath(rootPath);
    }

    public string? GetUnifiedDiff(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        var repoPath = Repository.Discover(_rootPath);
        if (repoPath is null)
            return null;

        using var repo = new Repository(repoPath);
        var workdir = repo.Info.WorkingDirectory ?? Path.GetDirectoryName(repo.Info.Path)!;

        // Our caller's path is relative to the loop root; libgit2 wants it relative to the repo
        // working directory (the root may be a subdirectory of the repo).
        var absolute = Path.GetFullPath(Path.Combine(_rootPath, relativePath));
        var repoRelative = Path.GetRelativePath(workdir, absolute).Replace('\\', '/');

        // ExplicitPathsOptions makes libgit2 include the listed path even when it is untracked
        // (a freshly created file), so created files show as all-added.
        var patch = repo.Diff.Compare<Patch>(
            repo.Head.Tip?.Tree,
            DiffTargets.WorkingDirectory,
            new[] { repoRelative },
            new ExplicitPathsOptions { ShouldFailOnUnmatchedPath = false },
            new CompareOptions { ContextLines = FullFileContext });

        var content = patch.Content;
        return string.IsNullOrEmpty(content) ? null : content;
    }
}
