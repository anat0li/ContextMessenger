namespace ContextMessenger.Protocol.Review;

/// <summary>
/// A reviewer comment delivered to the model inside the held response. Empty <see cref="Path"/>
/// with line 0 represents a general thread; otherwise the comment is anchored to a file position.
/// The <see cref="Id"/> lets the model reply to a specific thread in its next amend_patch.
/// </summary>
public sealed record ReviewerComment
{
    public required string Id { get; init; }

    public required string Path { get; init; }

    public int Line { get; init; }

    public required string Comment { get; init; }

    /// <summary>True when this thread represents an unresolved review issue that blocks acceptance.</summary>
    public bool OpenIssue { get; init; }
}
