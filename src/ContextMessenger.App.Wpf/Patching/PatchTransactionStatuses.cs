namespace ContextMessenger.App.Wpf.Patching;

/// <summary>
/// The patch transaction status strings the host cares about for review. These mirror
/// the values produced by <c>PatchTransactionService</c> so no mapping layer is needed.
/// Terminal outcomes (<c>accepted</c>) are not represented here: an accepted patch is
/// disposed and never held.
/// </summary>
public static class PatchTransactionStatuses
{
    public const string NeedsRevision = "needs_revision";
    public const string AwaitingAcceptance = "awaiting_acceptance";
    public const string Reverted = "reverted";

    public static bool IsHoldable(string? status) =>
        status is NeedsRevision or AwaitingAcceptance or Reverted;
}
