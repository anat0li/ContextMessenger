namespace ContextMessenger.Core.Patching;

public sealed record ProposePatchRequest
{
    public string? Title { get; init; }

    public string? Description { get; init; }

    public string? CommitMessage { get; init; }

    public IReadOnlyList<PatchFileOperation> Files { get; init; } = [];

    public IReadOnlyList<PatchEditOperation> Edits { get; init; } = [];

    public PatchPolicy Build { get; init; } = new();

    public PatchPolicy Tests { get; init; } = new();

    /// <summary>
    /// When true, a patch that passes build/tests is left applied-but-unstaged in an
    /// <c>awaiting_acceptance</c> state with the transaction kept open, instead of being
    /// staged and closed immediately. Acceptance then becomes an explicit follow-up
    /// (host toolbar action). This is a local host policy (per-root hold-for-review),
    /// never populated from model input.
    /// </summary>
    public bool DeferAcceptance { get; init; }
}
