using LibGit2Sharp;

namespace ContextMessenger.Patching.Tests;

public sealed class LibGit2SharpGitStatusServiceTests
{
    [Fact]
    public void GetStatus_returns_not_repository_for_plain_directory()
    {
        using var temp = new TempDirectory();
        var service = new LibGit2SharpGitStatusService(temp.Path);

        var status = service.GetStatus();

        Assert.False(status.IsRepository);
        Assert.False(status.IsClean);
        Assert.Empty(status.ChangedFiles);
    }

    [Fact]
    public void GetStatus_reports_clean_and_dirty_repository_states()
    {
        using var temp = new TempDirectory();
        Repository.Init(temp.Path);
        temp.CreateFile("tracked.txt", "one");
        using (var repo = new Repository(temp.Path))
        {
            Commands.Stage(repo, "tracked.txt");
            repo.Commit("initial", Signature(), Signature());
        }

        var service = new LibGit2SharpGitStatusService(temp.Path);
        var clean = service.GetStatus();

        Assert.True(clean.IsRepository);
        Assert.True(clean.IsClean);
        Assert.NotNull(clean.HeadSha);

        temp.CreateFile("tracked.txt", "two");
        temp.CreateFile("new.txt", "new");
        var dirty = service.GetStatus();

        Assert.False(dirty.IsClean);
        Assert.Contains(dirty.ChangedFiles, f => f is { Path: "tracked.txt", Status: "modified_unstaged" });
        Assert.Contains(dirty.ChangedFiles, f => f is { Path: "new.txt", Status: "untracked" });
    }

    [Fact]
    public void GetStatus_normalizes_staged_status_names()
    {
        using var temp = new TempDirectory();
        Repository.Init(temp.Path);
        temp.CreateFile("tracked.txt", "one");
        using (var repo = new Repository(temp.Path))
        {
            Commands.Stage(repo, "tracked.txt");
            repo.Commit("initial", Signature(), Signature());
        }

        temp.CreateFile("tracked.txt", "two");
        using (var repo = new Repository(temp.Path))
        {
            Commands.Stage(repo, "tracked.txt");
        }

        var service = new LibGit2SharpGitStatusService(temp.Path);
        var dirty = service.GetStatus();

        Assert.Contains(dirty.ChangedFiles, f => f is { Path: "tracked.txt", Status: "staged_modified" });
    }

    private static Signature Signature() =>
        new("ContextMessenger Tests", "tests@example.invalid", DateTimeOffset.UtcNow);
}
