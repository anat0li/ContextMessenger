namespace ContextMessenger.Core.Patching;

public sealed record PatchTransactionResult
{
    public required string PatchStatus { get; init; }

    public string? PatchId { get; init; }

    public int Revision { get; init; }

    public string? Title { get; init; }

    public string? Description { get; init; }

    public string? CommitMessage { get; init; }

    public bool Recovered { get; init; }

    public string? LastFailureStage { get; init; }

    public bool Applied { get; init; }

    public bool DiffVerified { get; init; }

    public PatchStageResult? Build { get; init; }

    public PatchStageResult? Tests { get; init; }

    public IReadOnlyList<PatchWarning> Warnings { get; init; } = [];

    public IReadOnlyList<PatchFileState> Files { get; init; } = [];
}
