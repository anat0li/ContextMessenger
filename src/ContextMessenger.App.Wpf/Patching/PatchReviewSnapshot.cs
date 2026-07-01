namespace ContextMessenger.App.Wpf.Patching;

/// <summary>
/// A point-in-time projection of the active patch for the review page, built from
/// <see cref="ContextMessenger.Core.Patching.IPatchTransactionService.Current"/>. Carries the
/// descriptive fields (shown in the Info dialog) and the list of changed files (left panel).
/// </summary>
public sealed record PatchReviewSnapshot
{
    public string? Title { get; init; }

    public string? Description { get; init; }

    public string? CommitMessage { get; init; }

    public string PatchId { get; init; } = "";

    public int Revision { get; init; }

    public string Status { get; init; } = "";

    public IReadOnlyList<PatchReviewFile> Files { get; init; } = [];

    public static PatchReviewSnapshot Empty { get; } = new();
}

/// <summary>One changed file in the active patch: its repo-relative path and operation.</summary>
public sealed record PatchReviewFile
{
    public required string Path { get; init; }

    /// <summary>One of <c>create</c>, <c>replace</c>, <c>delete</c> (lower-case, from core).</summary>
    public required string Operation { get; init; }
}
