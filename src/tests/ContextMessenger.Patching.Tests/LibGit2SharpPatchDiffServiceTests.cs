using LibGit2Sharp;

namespace ContextMessenger.Patching.Tests;

public sealed class LibGit2SharpPatchDiffServiceTests
{
    [Fact]
    public void GetUnifiedDiff_returns_null_outside_repository()
    {
        using var temp = new TempDirectory();
        var service = new LibGit2SharpPatchDiffService(temp.Path);

        Assert.Null(service.GetUnifiedDiff("anything.txt"));
    }

    [Fact]
    public void GetUnifiedDiff_returns_null_for_unchanged_path()
    {
        using var temp = new TempDirectory();
        Commit(temp, "tracked.txt", "one\n");
        var service = new LibGit2SharpPatchDiffService(temp.Path);

        Assert.Null(service.GetUnifiedDiff("tracked.txt"));
    }

    [Fact]
    public void GetUnifiedDiff_shows_added_and_removed_lines_for_unstaged_edit()
    {
        using var temp = new TempDirectory();
        Commit(temp, "tracked.txt", "one\ntwo\n");
        temp.CreateFile("tracked.txt", "one\ntwoChanged\n");

        var service = new LibGit2SharpPatchDiffService(temp.Path);
        var diff = service.GetUnifiedDiff("tracked.txt");

        Assert.NotNull(diff);
        Assert.Contains("-two", diff);
        Assert.Contains("+twoChanged", diff);
    }

    [Fact]
    public void GetUnifiedDiff_includes_distant_unchanged_lines_via_full_context()
    {
        using var temp = new TempDirectory();
        var original = string.Join("\n", Enumerable.Range(1, 12).Select(i => $"line{i}")) + "\n";
        Commit(temp, "f.txt", original);
        temp.CreateFile("f.txt", original.Replace("line6\n", "line6_CHANGED\n"));

        var diff = new LibGit2SharpPatchDiffService(temp.Path).GetUnifiedDiff("f.txt");

        Assert.NotNull(diff);
        Assert.Contains("+line6_CHANGED", diff);
        // Far from the change — only present because we request full context (default is 3 lines).
        Assert.Contains("line1", diff);
        Assert.Contains("line12", diff);
    }

    [Fact]
    public void GetUnifiedDiff_shows_all_added_for_created_untracked_file()
    {
        using var temp = new TempDirectory();
        Commit(temp, "tracked.txt", "one\n");
        temp.CreateFile("created.txt", "brand new\n");

        var service = new LibGit2SharpPatchDiffService(temp.Path);
        var diff = service.GetUnifiedDiff("created.txt");

        Assert.NotNull(diff);
        Assert.Contains("+brand new", diff);
    }

    private static void Commit(TempDirectory temp, string relativePath, string content)
    {
        if (Repository.Discover(temp.Path) is null)
            Repository.Init(temp.Path);

        temp.CreateFile(relativePath, content);
        using var repo = new Repository(temp.Path);
        Commands.Stage(repo, relativePath);
        var signature = new Signature("ContextMessenger Tests", "tests@example.invalid", DateTimeOffset.UtcNow);
        repo.Commit("commit", signature, signature);
    }
}
