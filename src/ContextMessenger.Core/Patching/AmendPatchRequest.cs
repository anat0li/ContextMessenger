namespace ContextMessenger.Core.Patching;

public sealed record AmendPatchRequest
{
    public required string PatchId { get; init; }

    public int BaseRevision { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<PatchFileOperation> Files { get; init; } = [];

    public IReadOnlyList<PatchEditOperation> Edits { get; init; } = [];

    public PatchPolicy? Build { get; init; }

    public PatchPolicy? Tests { get; init; }

    /// <summary>
    /// When true, an amendment that passes build/tests is left applied-but-unstaged in an
    /// <c>awaiting_acceptance</c> state with the transaction kept open, instead of being
    /// staged and closed immediately. See <see cref="ProposePatchRequest.DeferAcceptance"/>.
    /// Local host policy; never populated from model input.
    /// </summary>
    public bool DeferAcceptance { get; init; }
}
