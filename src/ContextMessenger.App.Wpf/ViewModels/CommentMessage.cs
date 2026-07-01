namespace ContextMessenger.App.Wpf.ViewModels;

/// <summary>Who authored a message in a comment thread.</summary>
public enum CommentAuthor
{
    Reviewer,
    Model,
}

/// <summary>
/// One message in a reviewer-comment thread. <see cref="AuthorLabel"/> is the display name — "You"
/// for the reviewer and the chat target's name (e.g. "ChatGPT") for the model side.
/// </summary>
public sealed record CommentMessage(CommentAuthor Author, string AuthorLabel, string Text);
