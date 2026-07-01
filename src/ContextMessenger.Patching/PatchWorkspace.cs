namespace ContextMessenger.Patching;

/// <summary>
/// Shared conventions for the patch workflow's on-disk scratch area. Build and test runs funnel
/// all of their output (via <c>--artifacts-path</c>/<c>--results-directory</c>) into a single
/// control directory under the active root, so it can be excluded from git status in one place.
/// Nothing under this directory is ever part of a patch.
/// </summary>
internal static class PatchWorkspace
{
    /// <summary>
    /// Name of the per-root control directory. Kept in one place so the build/test runners that
    /// write into it and <see cref="LibGit2SharpGitStatusService"/> that filters it cannot drift.
    /// </summary>
    public const string ControlDirectoryName = ".contextmessenger";
}
