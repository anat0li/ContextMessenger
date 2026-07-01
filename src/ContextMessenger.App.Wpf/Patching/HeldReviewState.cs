using ContextMessenger.App.Wpf.ViewModels;

namespace ContextMessenger.App.Wpf.Patching;

public sealed record HeldReviewState
{
    public required HeldPatchInteraction Interaction { get; init; }

    public IReadOnlyList<ReviewCommentState> Comments { get; init; } = [];

    public static HeldReviewState From(HeldPatchInteraction interaction, IEnumerable<ReviewComment> comments) => new()
    {
        Interaction = interaction,
        Comments = comments.Select(ReviewCommentState.From).ToArray(),
    };
}

public sealed record ReviewCommentState
{
    public required string Id { get; init; }

    public required string Path { get; init; }

    public int Line { get; init; }

    public bool Pending { get; init; }

    public bool OpenIssue { get; init; }

    public CommentAnchorStatus AnchorStatus { get; init; } = CommentAnchorStatus.Current;

    public string AnchorText { get; init; } = "";

    public IReadOnlyList<string> BeforeContext { get; init; } = [];

    public IReadOnlyList<string> AfterContext { get; init; } = [];

    public IReadOnlyList<CommentMessageState> Messages { get; init; } = [];

    public static ReviewCommentState From(ReviewComment comment) => new()
    {
        Id = comment.Id,
        Path = comment.Path,
        Line = comment.Line,
        Pending = comment.Pending,
        OpenIssue = comment.OpenIssue,
        AnchorStatus = comment.AnchorStatus,
        AnchorText = comment.AnchorText,
        BeforeContext = comment.BeforeContext,
        AfterContext = comment.AfterContext,
        Messages = comment.Messages.Select(CommentMessageState.From).ToArray(),
    };

    public ReviewComment ToComment()
    {
        var comment = new ReviewComment
        {
            Id = Id,
            Path = Path,
            Line = Line,
            Pending = Pending,
            OpenIssue = OpenIssue,
            AnchorStatus = AnchorStatus,
            AnchorText = AnchorText,
            BeforeContext = BeforeContext,
            AfterContext = AfterContext,
        };
        foreach (var message in Messages)
            comment.Messages.Add(message.ToMessage());

        return comment;
    }
}

public sealed record CommentMessageState
{
    public CommentAuthor Author { get; init; }

    public string AuthorLabel { get; init; } = "";

    public string Text { get; init; } = "";

    public static CommentMessageState From(CommentMessage message) => new()
    {
        Author = message.Author,
        AuthorLabel = message.AuthorLabel,
        Text = message.Text,
    };

    public CommentMessage ToMessage() => new(Author, AuthorLabel, Text);
}
