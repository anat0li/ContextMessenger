namespace ContextMessenger.Core.Patching;

public sealed record PatchValidationResult
{
    public bool Valid { get; init; }

    public string Mode { get; init; } = "";

    public string? PatchId { get; init; }

    public int? BaseRevision { get; init; }

    public bool Applied { get; init; }

    public bool DiffVerified { get; init; }

    public PatchStageResult Build { get; init; } = new() { Status = "skipped", Policy = "none" };

    public PatchStageResult Tests { get; init; } = new() { Status = "skipped", Policy = "none" };

    public IReadOnlyList<PatchWarning> Warnings { get; init; } = [];

    public IReadOnlyList<PatchFileState> Files { get; init; } = [];
}
