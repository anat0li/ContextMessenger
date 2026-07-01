using System.IO;
using ContextMessenger.App.Wpf.Patching;
using ContextMessenger.App.Wpf.Services;
using ContextMessenger.App.Wpf.ViewModels;
using ContextMessenger.Protocol.Dispatch;
using ContextMessenger.Protocol.Review;

namespace ContextMessenger.App.Wpf.Tests;

public sealed class PatchReviewServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "CmPatchReviewServiceTests_" + Guid.NewGuid().ToString("N"));

    private string StatePath => Path.Combine(_dir, "held-review.json");

    [Fact]
    public void Project_sets_router_target_updates_view_model_and_persists()
    {
        var service = NewService();
        var actions = new RecordingActions();
        var held = SampleInteraction();
        service.Store.Save(held);

        service.Project(actions);

        Assert.Same(actions, service.Router.Target);
        Assert.Same(held, service.PatchReview.Interaction);
        Assert.NotNull(new FileHeldReviewStore(StatePath).Load());
    }

    [Fact]
    public void Store_save_persists_immediately_before_projection()
    {
        var service = NewService();

        service.Store.Save(SampleInteraction());

        var state = new FileHeldReviewStore(StatePath).Load();
        Assert.NotNull(state);
        Assert.Equal("p-1", state.Interaction.PatchId);
    }

    [Fact]
    public void Project_clears_router_and_file_when_no_interaction_remains()
    {
        var service = NewService();
        var actions = new RecordingActions();
        service.Store.Save(SampleInteraction());
        service.Project(actions);

        service.Store.Clear();
        service.RefreshProjection();

        Assert.Null(service.Router.Target);
        Assert.False(File.Exists(StatePath));
    }

    [Fact]
    public void Restore_reloads_comments_and_returns_owner()
    {
        var state = SampleState();
        new FileHeldReviewStore(StatePath).Save(state);
        var service = NewService();

        var owner = service.Restore();

        Assert.NotNull(owner);
        Assert.Equal("ChatGPT", owner.TargetName);
        Assert.Equal("Repo", owner.RootName);
        Assert.Equal("p-1", service.Store.Current?.PatchId);
        var comment = Assert.Single(service.PatchReview.Comments);
        Assert.Equal("question", comment.Messages[0].Text);
    }

    [Fact]
    public void Restore_preserves_reply_turn_watermark_when_reprojected()
    {
        var state = SampleState() with
        {
            Interaction = SampleInteraction() with
            {
                ReplyTurn = 2,
                CommentReplies = [new PatchCommentReply { Id = "c-1", Reply = "answer" }],
            },
        };
        new FileHeldReviewStore(StatePath).Save(state);
        var service = NewService();
        var owner = service.Restore();
        Assert.NotNull(owner);
        var actions = new RecordingActions();

        service.Project(actions);

        var comment = Assert.Single(service.PatchReview.Comments);
        Assert.Equal(2, comment.Messages.Count);
        Assert.Equal("answer", comment.Messages[1].Text);
    }

    [Fact]
    public void Project_persists_model_reply_that_clears_last_open_issue()
    {
        var state = SampleState() with
        {
            Interaction = SampleInteraction() with { TransactionStatus = PatchTransactionStatuses.AwaitingAcceptance },
            Comments =
            [
                new ReviewCommentState
                {
                    Id = "c-1",
                    Path = "src/A.cs",
                    Line = 10,
                    OpenIssue = true,
                    Messages =
                    [
                        new CommentMessageState
                        {
                            Author = CommentAuthor.Reviewer,
                            AuthorLabel = "You",
                            Text = "open issue",
                        },
                    ],
                },
            ],
        };
        new FileHeldReviewStore(StatePath).Save(state);
        var service = NewService();
        Assert.NotNull(service.Restore());
        var actions = new RecordingActions();

        service.Store.Save(SampleInteraction() with
        {
            TransactionStatus = PatchTransactionStatuses.AwaitingAcceptance,
            ReplyTurn = 1,
            CommentReplies = [new PatchCommentReply { Id = "c-1", Reply = "fixed", OpenIssue = false }],
        });
        service.Project(actions);

        var persisted = new FileHeldReviewStore(StatePath).Load();
        Assert.NotNull(persisted);
        var comment = Assert.Single(persisted.Comments);
        Assert.False(comment.OpenIssue);
        Assert.Equal(2, comment.Messages.Count);
        Assert.Equal("fixed", comment.Messages[1].Text);
    }

    private PatchReviewService NewService() => new(new FileHeldReviewStore(StatePath));

    private static HeldReviewState SampleState() => new()
    {
        Interaction = SampleInteraction(),
        Comments =
        [
            new ReviewCommentState
            {
                Id = "c-1",
                Path = "src/A.cs",
                Line = 10,
                Pending = false,
                Messages =
                [
                    new CommentMessageState
                    {
                        Author = CommentAuthor.Reviewer,
                        AuthorLabel = "You",
                        Text = "question",
                    },
                    new CommentMessageState
                    {
                        Author = CommentAuthor.Model,
                        AuthorLabel = "ChatGPT",
                        Text = "answer",
                    },
                ],
            },
        ],
    };

    private static HeldPatchInteraction SampleInteraction() => new()
    {
        RootName = "Repo",
        TargetName = "ChatGPT",
        PatchId = "p-1",
        Revision = 1,
        TransactionStatus = PatchTransactionStatuses.NeedsRevision,
        HeldResponseText = "BEGIN_RESPONSE\n{}\nEND_RESPONSE",
        Phase = PatchInteractionPhase.Reviewing,
    };

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { }
    }

    private sealed class RecordingActions : IHeldPatchActions
    {
        public Task SendAsync(IReadOnlyList<ReviewerComment> comments) => Task.CompletedTask;
        public Task AcceptAsync() => Task.CompletedTask;
        public Task RevertAsync() => Task.CompletedTask;
        public Task RefreshAsync() => Task.CompletedTask;
        public PatchReviewSnapshot GetSnapshot() => new()
        {
            Files = [new PatchReviewFile { Path = "src/A.cs", Operation = "replace" }],
        };
        public string? GetFileDiff(string path) => null;
        public string? GetFileContent(string path) => null;
    }
}
