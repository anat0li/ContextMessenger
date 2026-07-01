using ContextMessenger.App.Wpf.Patching;

namespace ContextMessenger.App.Wpf.Tests;

public sealed class PatchTreeBuilderTests
{
    [Fact]
    public void Nested_paths_fold_into_folders_with_files_as_leaves()
    {
        var files = new[]
        {
            new PatchReviewFile { Path = "src/App/Util.cs", Operation = "create" },
            new PatchReviewFile { Path = "src/App/Main.cs", Operation = "replace" },
            new PatchReviewFile { Path = "README.md", Operation = "replace" },
        };

        var roots = PatchTreeBuilder.Build(files);

        // Folders sort before files at each level: src (folder) before README.md (file).
        Assert.Equal(2, roots.Count);
        var src = roots[0];
        Assert.True(src.IsFolder);
        Assert.Equal("src", src.Name);

        var readme = roots[1];
        Assert.False(readme.IsFolder);
        Assert.Equal("README.md", readme.RelativePath);

        var app = Assert.Single(src.Children);
        Assert.Equal("App", app.Name);
        Assert.Equal(2, app.Children.Count);
        Assert.All(app.Children, c => Assert.False(c.IsFolder));
        Assert.Equal("src/App/Main.cs", app.Children[0].RelativePath); // Main before Util (alpha)
        Assert.Equal("replace", app.Children[0].Operation);
    }

    [Fact]
    public void Empty_input_yields_no_nodes()
    {
        Assert.Empty(PatchTreeBuilder.Build([]));
    }
}
