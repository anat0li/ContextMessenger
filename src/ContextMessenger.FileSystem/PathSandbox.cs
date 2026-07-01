using ContextMessenger.Core.FileSystem;

namespace ContextMessenger.FileSystem;

public sealed class PathSandbox
{
    public string Root { get; }

    public PathSandbox(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Root path must be non-empty.", nameof(rootPath));

        var full = Path.GetFullPath(rootPath);
        if (!Directory.Exists(full))
            throw new DirectoryNotFoundException($"Sandbox root does not exist: {full}");

        Root = TrimTrailingSeparator(full);
    }

    public string ResolveAbsolute(string pathUnderRoot)
    {
        var input = string.IsNullOrEmpty(pathUnderRoot) ? "." : pathUnderRoot;
        var combined = Path.IsPathRooted(input) ? input : Path.Combine(Root, input);
        var full = TrimTrailingSeparator(Path.GetFullPath(combined));

        if (!IsInsideRoot(full))
            throw new PathOutsideSandboxException(pathUnderRoot);

        return full;
    }

    /// <summary>
    /// Resolves a path for a write operation. Beyond the lexical sandbox check that
    /// <see cref="ResolveAbsolute"/> performs, this rejects any path that traverses a reparse
    /// point (symbolic link or junction) below the root, because such a link can redirect the
    /// real write location outside the sandbox. Reads deliberately tolerate in-repo links;
    /// writes must not be able to follow one out.
    /// </summary>
    public string ResolveForWrite(string pathUnderRoot)
    {
        var full = ResolveAbsolute(pathUnderRoot);
        if (TraversesReparsePoint(full))
            throw new PathOutsideSandboxException(pathUnderRoot);

        return full;
    }

    private bool TraversesReparsePoint(string absolutePath)
    {
        // Walk existing components from the target up to (but not including) the root. A reparse
        // point anywhere on that chain can point outside the sandbox even though the lexical path
        // sits within it. The root itself is intentionally not checked: the user may legitimately
        // reach it through a link, and it is everything created *below* the root that must stay
        // contained.
        var current = TrimTrailingSeparator(absolutePath);
        while (current.Length > Root.Length)
        {
            if (IsReparsePoint(current))
                return true;

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || parent.Length >= current.Length)
                break;

            current = parent;
        }

        return false;
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            // A component that does not exist yet (a to-be-created file or intermediate directory)
            // cannot be a link, so a missing-path exception is treated as safe.
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    public bool IsInsideRoot(string absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath)) return false;
        var full = TrimTrailingSeparator(Path.GetFullPath(absolutePath));

        if (string.Equals(full, Root, StringComparison.OrdinalIgnoreCase))
            return true;

        var rootWithSep = Root + Path.DirectorySeparatorChar;
        return full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase);
    }

    public string ToRelative(string absolutePath)
    {
        var full = ResolveAbsolute(absolutePath);
        if (string.Equals(full, Root, StringComparison.OrdinalIgnoreCase))
            return ".";

        var rel = Path.GetRelativePath(Root, full);
        return rel.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string TrimTrailingSeparator(string p)
    {
        if (p.Length < 2) return p;
        var last = p[^1];
        if ((last == '/' || last == '\\') && p[^2] != ':')
            return p[..^1];
        return p;
    }
}
