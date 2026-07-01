namespace ContextMessenger.Core.Patching;

public sealed record ValidatePatchRequest
{
    public string? PatchId { get; init; }

    public int? BaseRevision { get; init; }

    public IReadOnlyList<PatchFileOperation> Files { get; init; } = [];

    public IReadOnlyList<PatchEditOperation> Edits { get; init; } = [];

    public PatchPolicy? Build { get; init; }

    public PatchPolicy? Tests { get; init; }
}
