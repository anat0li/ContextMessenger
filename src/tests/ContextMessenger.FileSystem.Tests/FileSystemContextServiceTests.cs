using ContextMessenger.Core.FileSystem;

namespace ContextMessenger.FileSystem.Tests;

public sealed class FileSystemContextServiceTests
{
    [Fact]
    public void Service_implements_IFileSystemService()
    {
        using var temp = new TempDirectory();
        IFileSystemService service = new FileSystemContextService(temp.Path);
        Assert.NotNull(service);
    }

    [Fact]
    public void Service_dispatches_to_each_subservice_via_one_facade()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("src/a.cs", "needle in code");
        temp.CreateFile("src/b.cs", "no match here");

        IFileSystemService service = new FileSystemContextService(temp.Path);

        var tree = service.GetTree(new TreeQuery(".") { MaxDepth = 2 });
        Assert.Equal(".", tree.Name);

        var content = service.ReadFile("src/a.cs");
        Assert.Equal("needle in code", content.Content);

        var matches = service.SearchText(new SearchQuery("needle"));
        var match = Assert.Single(matches);
        Assert.Equal("src/a.cs", match.RelativePath);

        var files = service.ListFiles(new ListFilesQuery());
        Assert.Equal(2, files.Count);
    }

    [Fact]
    public void Service_throws_when_root_does_not_exist()
    {
        var ghost = Path.Combine(Path.GetTempPath(), "ContextMessenger_" + Guid.NewGuid().ToString("N"));
        Assert.Throws<DirectoryNotFoundException>(() => new FileSystemContextService(ghost));
    }
}
