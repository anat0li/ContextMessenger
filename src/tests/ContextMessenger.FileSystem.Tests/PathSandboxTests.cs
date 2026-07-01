using ContextMessenger.Core.FileSystem;

namespace ContextMessenger.FileSystem.Tests;

public sealed class PathSandboxTests
{
    [Fact]
    public void Constructor_throws_when_path_empty()
    {
        Assert.Throws<ArgumentException>(() => new PathSandbox(""));
    }

    [Fact]
    public void Constructor_throws_when_path_does_not_exist()
    {
        var ghost = Path.Combine(Path.GetTempPath(), "ContextMessenger_" + Guid.NewGuid().ToString("N"));
        Assert.Throws<DirectoryNotFoundException>(() => new PathSandbox(ghost));
    }

    [Fact]
    public void Root_is_normalized_without_trailing_separator()
    {
        using var temp = new TempDirectory();
        var sandbox = new PathSandbox(temp.Path + Path.DirectorySeparatorChar);
        Assert.False(sandbox.Root.EndsWith(Path.DirectorySeparatorChar));
        Assert.False(sandbox.Root.EndsWith('/'));
    }

    [Fact]
    public void ResolveAbsolute_dot_returns_root()
    {
        using var temp = new TempDirectory();
        var sandbox = new PathSandbox(temp.Path);
        Assert.Equal(sandbox.Root, sandbox.ResolveAbsolute("."));
    }

    [Fact]
    public void ResolveAbsolute_handles_forward_slashes()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("src/inner/file.cs", "x");
        var sandbox = new PathSandbox(temp.Path);
        var resolved = sandbox.ResolveAbsolute("src/inner/file.cs");
        Assert.True(File.Exists(resolved));
    }

    [Fact]
    public void ResolveAbsolute_blocks_parent_directory_escape()
    {
        using var temp = new TempDirectory();
        var sandbox = new PathSandbox(temp.Path);
        Assert.Throws<PathOutsideSandboxException>(() => sandbox.ResolveAbsolute("../escape"));
    }

    [Fact]
    public void ResolveAbsolute_blocks_absolute_path_outside_root()
    {
        using var temp = new TempDirectory();
        var sandbox = new PathSandbox(temp.Path);
        var outside = Path.GetTempPath();
        Assert.Throws<PathOutsideSandboxException>(() => sandbox.ResolveAbsolute(outside));
    }

    [Fact]
    public void ResolveAbsolute_allows_absolute_path_inside_root()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("a.txt", "x");
        var sandbox = new PathSandbox(temp.Path);
        var insideAbs = Path.Combine(temp.Path, "a.txt");
        Assert.Equal(insideAbs, sandbox.ResolveAbsolute(insideAbs));
    }

    [Fact]
    public void ResolveForWrite_allows_normal_path_inside_root()
    {
        using var temp = new TempDirectory();
        temp.CreateDir("src");
        var sandbox = new PathSandbox(temp.Path);
        Assert.Equal(
            System.IO.Path.Combine(sandbox.Root, "src", "new.cs"),
            sandbox.ResolveForWrite("src/new.cs"));
    }

    [Fact]
    public void ResolveForWrite_blocks_symlink_traversal_out_of_root()
    {
        using var root = new TempDirectory();
        using var outside = new TempDirectory();
        var linkPath = System.IO.Path.Combine(root.Path, "link");
        try
        {
            Directory.CreateSymbolicLink(linkPath, outside.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Creating a symbolic link needs privileges (Developer Mode / admin on Windows) that
            // may be unavailable here. xUnit v2 has no runtime skip, so treat it as inconclusive.
            return;
        }

        var sandbox = new PathSandbox(root.Path);

        // A write through the link would land in `outside`, so it must be rejected...
        Assert.Throws<PathOutsideSandboxException>(() => sandbox.ResolveForWrite("link/evil.txt"));
        // ...while a read through the same link is deliberately tolerated.
        Assert.Equal(
            System.IO.Path.Combine(sandbox.Root, "link", "evil.txt"),
            sandbox.ResolveAbsolute("link/evil.txt"));
    }

    [Fact]
    public void IsInsideRoot_true_for_root_itself()
    {
        using var temp = new TempDirectory();
        var sandbox = new PathSandbox(temp.Path);
        Assert.True(sandbox.IsInsideRoot(temp.Path));
    }

    [Fact]
    public void IsInsideRoot_false_for_sibling_with_shared_prefix()
    {
        using var temp = new TempDirectory();
        var siblingDir = temp.Path + "_sibling";
        Directory.CreateDirectory(siblingDir);
        try
        {
            var sandbox = new PathSandbox(temp.Path);
            Assert.False(sandbox.IsInsideRoot(siblingDir));
        }
        finally { Directory.Delete(siblingDir); }
    }

    [Fact]
    public void ToRelative_returns_dot_for_root()
    {
        using var temp = new TempDirectory();
        var sandbox = new PathSandbox(temp.Path);
        Assert.Equal(".", sandbox.ToRelative(temp.Path));
    }

    [Fact]
    public void ToRelative_uses_forward_slashes()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("src/inner/file.cs", "x");
        var sandbox = new PathSandbox(temp.Path);
        var abs = Path.Combine(temp.Path, "src", "inner", "file.cs");
        Assert.Equal("src/inner/file.cs", sandbox.ToRelative(abs));
    }
}
