namespace ContextMessenger.Protocol.Dispatch;

/// <summary>
/// A model comment taken from an <c>amend_patch</c> request's <c>commentReplies</c>.
/// Matching ids append to existing review threads; unknown ids open model-originated threads.
/// </summary>
public sealed record PatchCommentReply
{
    public required string Id { get; init; }

    public required string Reply { get; init; }

    public string Path { get; init; } = "";

    public int Line { get; init; }

    /// <summary>When present, sets the thread's unresolved review issue state.</summary>
    public bool? OpenIssue { get; init; }
}
