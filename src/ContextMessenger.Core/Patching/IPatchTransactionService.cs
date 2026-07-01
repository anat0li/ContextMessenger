namespace ContextMessenger.Core.Patching;

public interface IPatchTransactionService
{
    bool HasActivePatch { get; }

    /// <summary>
    /// When true, a patch that passes build/tests is held in <c>awaiting_acceptance</c>
    /// (applied, unstaged, transaction open) instead of being staged and closed. Settable
    /// so the host can flip per-root hold-for-review at runtime.
    /// </summary>
    bool DeferAcceptanceByDefault { get; set; }

    PatchTransactionResult Propose(ProposePatchRequest request);

    PatchTransactionResult Amend(AmendPatchRequest request);

    PatchValidationResult Validate(ValidatePatchRequest request);

    PatchTransactionResult Current();

    /// <summary>Finalize a deferred (awaiting_acceptance) patch: stage the files and close the transaction.</summary>
    PatchTransactionResult Accept(string patchId);

    PatchTransactionResult Revert(string patchId);
}
