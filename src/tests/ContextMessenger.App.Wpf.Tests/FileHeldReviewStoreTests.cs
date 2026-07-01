using System.IO;
using ContextMessenger.App.Wpf.Patching;
using ContextMessenger.App.Wpf.Services;
using ContextMessenger.App.Wpf.ViewModels;
using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.App.Wpf.Tests;

public sealed class FileHeldReviewStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "CmHeldReviewStoreTests_" + Guid.NewGuid().ToString("N"));

    private string StatePath => Path.Combine(_dir, "held-review.json");

    [Fact]
    public void Save_and_load_round_trips_interaction_and_comment_thread()
    {
        var store = new FileHeldReviewStore(StatePath);
        var state = new HeldReviewState
        {
            Interaction = SampleInteraction(),
            Comments =
            [
                new ReviewCommentState
                {
                    Id = "c-1",
                    Path = "src/A.cs",
                    Line = 12,
                    Pending = true,
                    OpenIssue = true,
                    Messages =
                    [
                        new CommentMessageState { Author = CommentAuthor.Reviewer, AuthorLabel = "You", Text = "question" },
                        new CommentMessageState { Author = CommentAuthor.Model, AuthorLabel = "ChatGPT", Text = "answer" },
                    ],
                },
            ],
        };

        store.Save(state);
        var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal("p-1", loaded.Interaction.PatchId);
        Assert.Equal("passed", loaded.Interaction.BuildSummary.Status);
        Assert.Equal("passed", loaded.Interaction.TestSummary.Status);
        Assert.Equal("CS0168", Assert.Single(loaded.Interaction.BuildWarnings).Code);
        var comment = Assert.Single(loaded.Comments);
        Assert.True(comment.Pending);
        Assert.True(comment.OpenIssue);
        Assert.Equal(2, comment.Messages.Count);
        Assert.Equal("answer", comment.Messages[1].Text);
    }

    [Fact]
    public void Missing_or_corrupt_file_loads_as_null()
    {
        var store = new FileHeldReviewStore(StatePath);
        Assert.Null(store.Load());

        Directory.CreateDirectory(_dir);
        File.WriteAllText(StatePath, "{ invalid");

        Assert.Null(store.Load());
    }

    [Fact]
    public void Clear_and_save_null_delete_the_file()
    {
        var store = new FileHeldReviewStore(StatePath);
        store.Save(new HeldReviewState { Interaction = SampleInteraction() });
        Assert.True(File.Exists(StatePath));

        store.Clear();
        Assert.False(File.Exists(StatePath));

        store.Save(new HeldReviewState { Interaction = SampleInteraction() });
        store.Save(null);
        Assert.False(File.Exists(StatePath));
    }

    private static HeldPatchInteraction SampleInteraction() => new()
    {
        RootName = "Repo",
        TargetName = "ChatGPT",
        PatchId = "p-1",
        Revision = 1,
        TransactionStatus = PatchTransactionStatuses.NeedsRevision,
        HeldResponseText = "BEGIN_RESPONSE\n{}\nEND_RESPONSE",
        BuildWarnings = [new PatchBuildWarning { Code = "CS0168", Path = "src/A.cs", Line = 12, Message = "unused" }],
        BuildSummary = new PatchStageSummary { Status = "passed", Policy = "solution" },
        TestSummary = new PatchStageSummary { Status = "passed", TotalTests = 1, PassedTests = 1 },
    };

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { }
    }
}
