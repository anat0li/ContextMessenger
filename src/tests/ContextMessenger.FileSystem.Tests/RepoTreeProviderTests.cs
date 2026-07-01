using ContextMessenger.Core.FileSystem;

namespace ContextMessenger.FileSystem.Tests;

public sealed class RepoTreeProviderTests
{
    [Fact]
    public void GetTree_returns_immediate_children_at_depth_one()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("a.txt", "x");
        temp.CreateFile("src/b.cs", "x");
        temp.CreateFile("src/inner/c.cs", "x");

        var provider = new RepoTreeProvider(new PathSandbox(temp.Path));
        var tree = provider.GetTree(new TreeQuery(".") { MaxDepth = 1 });

        Assert.True(tree.IsDirectory);
        Assert.Equal(".", tree.Name);
        var names = tree.Children.Select(c => c.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "a.txt", "src" }, names);
        var src = tree.Children.Single(c => c.Name == "src");
        Assert.Empty(src.Children); // depth limit reached
    }

    [Fact]
    public void GetTree_recurses_to_max_depth()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("src/inner/c.cs", "x");

        var provider = new RepoTreeProvider(new PathSandbox(temp.Path));
        var tree = provider.GetTree(new TreeQuery(".") { MaxDepth = 3 });

        var src = tree.Children.Single(c => c.Name == "src");
        var inner = src.Children.Single(c => c.Name == "inner");
        var file = inner.Children.Single(c => c.Name == "c.cs");
        Assert.False(file.IsDirectory);
        Assert.Equal("src/inner/c.cs", file.RelativePath);
    }

    [Fact]
    public void GetTree_skips_default_excluded_directories()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("bin/Debug/junk.dll", "x");
        temp.CreateFile("obj/junk.cache", "x");
        temp.CreateFile(".git/HEAD", "x");
        temp.CreateFile("src/file.cs", "x");

        var provider = new RepoTreeProvider(new PathSandbox(temp.Path));
        var tree = provider.GetTree(new TreeQuery(".") { MaxDepth = 5 });

        var topNames = tree.Children.Select(c => c.Name).ToArray();
        Assert.Contains("src", topNames);
        Assert.DoesNotContain("bin", topNames);
        Assert.DoesNotContain("obj", topNames);
        Assert.DoesNotContain(".git", topNames);
    }

    [Fact]
    public void GetTree_applies_user_include_globs_to_files()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("src/keep.cs", "x");
        temp.CreateFile("src/skip.txt", "x");

        var provider = new RepoTreeProvider(new PathSandbox(temp.Path));
        var tree = provider.GetTree(new TreeQuery(".")
        {
            MaxDepth = 5,
            IncludeGlobs = ["**/*.cs"],
        });

        var src = tree.Children.Single(c => c.Name == "src");
        var fileNames = src.Children.Where(c => !c.IsDirectory).Select(c => c.Name).ToArray();
        Assert.Equal(new[] { "keep.cs" }, fileNames);
    }

    [Fact]
    public void GetTree_applies_globs_relative_to_requested_path()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("src/app/keep.cs", "x");
        temp.CreateFile("src/tests/skip.cs", "x");
        temp.CreateFile("tests/outside.cs", "x");

        var provider = new RepoTreeProvider(new PathSandbox(temp.Path));
        var tree = provider.GetTree(new TreeQuery("src")
        {
            MaxDepth = 5,
            IncludeGlobs = ["**/*.cs"],
            ExcludeGlobs = ["tests/**"],
        });

        var app = Assert.Single(tree.Children, c => c.Name == "app");
        Assert.Equal("keep.cs", Assert.Single(app.Children).Name);
        Assert.DoesNotContain(tree.Children, c => c.Name == "tests");
    }

    [Fact]
    public void GetTree_returns_single_node_for_file_path()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("a.txt", "x");

        var provider = new RepoTreeProvider(new PathSandbox(temp.Path));
        var tree = provider.GetTree(new TreeQuery("a.txt"));

        Assert.False(tree.IsDirectory);
        Assert.Equal("a.txt", tree.Name);
    }

    [Fact]
    public void GetTree_throws_when_path_does_not_exist()
    {
        using var temp = new TempDirectory();
        var provider = new RepoTreeProvider(new PathSandbox(temp.Path));
        Assert.Throws<DirectoryNotFoundException>(() =>
            provider.GetTree(new TreeQuery("nope")));
    }

    [Fact]
    public void GetTree_throws_for_negative_depth()
    {
        using var temp = new TempDirectory();
        var provider = new RepoTreeProvider(new PathSandbox(temp.Path));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            provider.GetTree(new TreeQuery(".") { MaxDepth = -1 }));
    }

    [Fact]
    public void Render_produces_indented_text()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("src/a.cs", "x");
        temp.CreateFile("src/b.cs", "x");

        var provider = new RepoTreeProvider(new PathSandbox(temp.Path));
        var tree = provider.GetTree(new TreeQuery(".") { MaxDepth = 2 });
        var rendered = TreeRenderer.Render(tree);

        Assert.Contains("./", rendered);
        Assert.Contains("src/", rendered);
        Assert.Contains("a.cs", rendered);
        Assert.Contains("b.cs", rendered);
    }
}
