using ContextMessenger.Core.ProjectInfo;
using LibGit2Sharp;

namespace ContextMessenger.FileSystem;

public sealed class LibGit2SharpGitRepositoryInfoProvider : IGitRepositoryInfoProvider
{
    public GitInfo GetGitInfo(string rootPath)
    {
        try
        {
            var discovered = Repository.Discover(rootPath);
            if (string.IsNullOrWhiteSpace(discovered))
                return new GitInfo { IsRepository = false };

            using var repo = new Repository(discovered);
            if (!IsInsideRoot(rootPath, repo.Info.WorkingDirectory))
                return new GitInfo { IsRepository = false };

            return new GitInfo
            {
                IsRepository = true,
                Branch = repo.Info.IsHeadDetached ? null : repo.Head.FriendlyName,
                HeadSha = repo.Head.Tip?.Sha,
                IsDirty = repo.RetrieveStatus(new StatusOptions
                {
                    IncludeIgnored = false,
                    IncludeUnaltered = false,
                    IncludeUntracked = true,
                    RecurseIgnoredDirs = false,
                    RecurseUntrackedDirs = true,
                }).IsDirty,
            };
        }
        catch (Exception)
        {
            return new GitInfo { IsRepository = false, IsDirty = null };
        }
    }

    private static bool IsInsideRoot(string rootPath, string? candidatePath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
            return false;

        var root = TrimTrailingSeparator(Path.GetFullPath(rootPath));
        var candidate = TrimTrailingSeparator(Path.GetFullPath(candidatePath));

        if (string.Equals(root, candidate, StringComparison.OrdinalIgnoreCase))
            return true;

        var rootWithSep = root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimTrailingSeparator(string path)
    {
        if (path.Length < 2) return path;
        var last = path[^1];
        if ((last == '/' || last == '\\') && path[^2] != ':')
            return path[..^1];
        return path;
    }
}
