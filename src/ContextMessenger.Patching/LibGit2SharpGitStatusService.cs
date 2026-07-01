using ContextMessenger.Core.Patching;
using LibGit2Sharp;

namespace ContextMessenger.Patching;

public sealed class LibGit2SharpGitStatusService : IGitStatusService
{
    private readonly string _rootPath;

    public LibGit2SharpGitStatusService(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Root path is required.", nameof(rootPath));

        _rootPath = Path.GetFullPath(rootPath);
    }

    public GitStatusInfo GetStatus()
    {
        var repoPath = Repository.Discover(_rootPath);
        if (repoPath is null)
            return new GitStatusInfo { IsRepository = false, IsClean = false };

        using var repo = new Repository(repoPath);
        var files = repo.RetrieveStatus(new StatusOptions
            {
                IncludeIgnored = false,
                IncludeUntracked = true,
                RecurseUntrackedDirs = true,
            })
            .Where(e => e.State != FileStatus.Ignored && e.State != FileStatus.Unaltered)
            .Select(ToStatusFile)
            .Where(f => f is not null)
            .Cast<GitStatusFile>()
            .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new GitStatusInfo
        {
            IsRepository = true,
            IsClean = files.Length == 0,
            Branch = repo.Head.FriendlyName,
            HeadSha = repo.Head.Tip?.Sha,
            ChangedFiles = files,
        };

        GitStatusFile? ToStatusFile(StatusEntry entry)
        {
            var workdir = repo.Info.WorkingDirectory ?? Path.GetDirectoryName(repo.Info.Path)!;
            var absolute = Path.GetFullPath(Path.Combine(workdir, entry.FilePath));
            if (!IsUnderRoot(absolute, _rootPath))
                return null;

            var relative = Path.GetRelativePath(_rootPath, absolute).Replace('\\', '/');
            // The patch workflow funnels all build/test output into this control directory under
            // the root. It is never part of a patch, so exclude it from status; otherwise its
            // untracked files would make the tree look dirty and break the propose/revert
            // clean-tree checks and the diff verifier.
            if (IsUnderControlDirectory(relative))
                return null;

            return new GitStatusFile
            {
                Path = relative,
                Status = NormalizeStatus(entry.State),
            };
        }
    }

    private static bool IsUnderControlDirectory(string relativePath) =>
        relativePath.Equals(PatchWorkspace.ControlDirectoryName, StringComparison.OrdinalIgnoreCase) ||
        relativePath.StartsWith(PatchWorkspace.ControlDirectoryName + "/", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeStatus(FileStatus status)
    {
        if (status.HasFlag(FileStatus.Conflicted)) return "conflicted";
        if (status.HasFlag(FileStatus.NewInIndex)) return "staged_new";
        if (status.HasFlag(FileStatus.ModifiedInIndex)) return "staged_modified";
        if (status.HasFlag(FileStatus.DeletedFromIndex)) return "staged_deleted";
        if (status.HasFlag(FileStatus.RenamedInIndex)) return "staged_renamed";
        if (status.HasFlag(FileStatus.TypeChangeInIndex)) return "staged_type_changed";
        if (status.HasFlag(FileStatus.NewInWorkdir)) return "untracked";
        if (status.HasFlag(FileStatus.ModifiedInWorkdir)) return "modified_unstaged";
        if (status.HasFlag(FileStatus.DeletedFromWorkdir)) return "deleted_unstaged";
        if (status.HasFlag(FileStatus.RenamedInWorkdir)) return "renamed_unstaged";
        if (status.HasFlag(FileStatus.TypeChangeInWorkdir)) return "type_changed_unstaged";
        if (status.HasFlag(FileStatus.Unreadable)) return "unreadable";
        return "changed";
    }

    private static bool IsUnderRoot(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullRoot, comparison) ||
               string.Equals(Path.TrimEndingDirectorySeparator(fullPath), Path.TrimEndingDirectorySeparator(fullRoot), comparison);
    }
}
