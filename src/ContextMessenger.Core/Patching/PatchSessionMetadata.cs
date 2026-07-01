namespace ContextMessenger.Core.Patching;

public sealed record PatchSessionMetadata
{
    public required string PatchId { get; init; }

    public required string RootName { get; init; }

    public required string Status { get; init; }

    public int Revision { get; init; }

    public string? Title { get; init; }

    public string? Description { get; init; }

    public string? CommitMessage { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime UpdatedAtUtc { get; init; }

    public required string BaseHeadSha { get; init; }

    public string? LastFailureStage { get; init; }

    public PatchPolicy BuildPolicy { get; init; } = new();

    public PatchPolicy TestPolicy { get; init; } = new();

    public PatchStageResult? LastBuild { get; init; }

    public PatchStageResult? LastTests { get; init; }

    /// <summary>
    /// The files the patch touched, with their original operation and content-hash anchors.
    /// Persisting these lets crash recovery rebuild the patch faithfully (preserving the
    /// optimistic-concurrency guards used by amend) instead of inferring the file set from the
    /// dirty working tree with the hashes lost. Empty for metadata written before this existed.
    /// </summary>
    public IReadOnlyList<PatchSessionFile> Files { get; init; } = [];
}

public sealed record PatchSessionFile
{
    public required string Path { get; init; }

    public required string Operation { get; init; }

    public string? OldContentHash { get; init; }

    public int LastRevision { get; init; }
}
