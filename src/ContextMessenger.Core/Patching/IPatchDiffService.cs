namespace ContextMessenger.Core.Patching;

/// <summary>
/// Produces the unified diff of an applied (unstaged) patch file against HEAD, for display on
/// the review page. Returns <c>null</c> when there is nothing to show (path unchanged in the
/// working tree, e.g. a rolled-back patch, or not a git repository).
/// </summary>
public interface IPatchDiffService
{
    string? GetUnifiedDiff(string relativePath);
}
