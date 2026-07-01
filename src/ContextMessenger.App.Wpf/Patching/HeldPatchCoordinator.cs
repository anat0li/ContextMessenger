namespace ContextMessenger.App.Wpf.Patching;

/// <summary>
/// Maps a patch outcome to a hold-or-deliver decision and maintains the single active
/// <see cref="HeldPatchInteraction"/>. Pure host-side policy: it consults the per-outcome
/// <see cref="PatchHoldRequest.HoldEnabled"/> flag (the latched per-root setting) and the
/// outcome status, and never touches the chat target or the patch service directly.
/// </summary>
public sealed class HeldPatchCoordinator
{
    private const string AcceptedStatus = "accepted";

    private readonly IHeldPatchInteractionStore _store;

    public HeldPatchCoordinator(IHeldPatchInteractionStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public PatchHoldDecision Evaluate(PatchHoldRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var outcome = request.Outcome;

        // Terminal model outcomes: the patch is gone (accepted+staged, or reverted+disposed).
        // Close any open review page.
        if (outcome.PatchStatus is AcceptedStatus or PatchTransactionStatuses.Reverted)
        {
            if (_store.Current is not null)
                _store.Clear();
            return PatchHoldDecision.Deliver;
        }

        // Page-worthy active states. A review page is created for any proposed patch that is
        // not immediately accepted/reverted+disposed, regardless of the hold setting. The
        // hold setting only decides whether the response is held or delivered.
        if (outcome.PatchStatus is not (PatchTransactionStatuses.NeedsRevision or PatchTransactionStatuses.AwaitingAcceptance))
            return PatchHoldDecision.Deliver;

        _store.Save(BuildOrUpdate(request));
        return request.HoldEnabled ? PatchHoldDecision.Hold : PatchHoldDecision.Deliver;
    }

    private HeldPatchInteraction BuildOrUpdate(PatchHoldRequest request)
    {
        var outcome = request.Outcome;
        // Held → the human holds the floor (Reviewing). Delivered (review off) → the response
        // went to the model, so we are waiting on its reply.
        var phase = request.HoldEnabled ? PatchInteractionPhase.Reviewing : PatchInteractionPhase.AwaitingModelReply;
        var entry = new PatchInteractionEntry
        {
            Direction = PatchInteractionDirection.Inbound,
            Summary = $"{outcome.CommandType} -> {outcome.PatchStatus}",
            Revision = outcome.Revision,
        };

        var existing = _store.Current;
        if (existing is not null && string.Equals(existing.PatchId, outcome.PatchId, StringComparison.Ordinal))
        {
            // Same patch evolving (e.g., needs_revision amended to awaiting_acceptance):
            // update status/revision, append history.
            return existing with
            {
                RequestId = outcome.RequestId,
                CommandType = outcome.CommandType,
                TransactionStatus = outcome.PatchStatus,
                Revision = outcome.Revision,
                Phase = phase,
                HeldResponseText = request.ResponseText,
                History = [.. existing.History, entry],
                BuildErrors = outcome.BuildErrors,
                BuildWarnings = outcome.BuildWarnings,
                BuildSummary = outcome.BuildSummary,
                TestFailures = outcome.TestFailures,
                TestSummary = outcome.TestSummary,
                CommentReplies = outcome.CommentReplies,
                ReplyTurn = existing.ReplyTurn + 1,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
        }

        return new HeldPatchInteraction
        {
            RootName = request.RootName,
            TargetName = request.TargetName,
            PatchId = outcome.PatchId ?? "",
            RequestId = outcome.RequestId,
            CommandType = outcome.CommandType,
            Revision = outcome.Revision,
            TransactionStatus = outcome.PatchStatus,
            Phase = phase,
            HeldResponseText = request.ResponseText,
            History = [entry],
            BuildErrors = outcome.BuildErrors,
            BuildWarnings = outcome.BuildWarnings,
            BuildSummary = outcome.BuildSummary,
            TestFailures = outcome.TestFailures,
            TestSummary = outcome.TestSummary,
            CommentReplies = outcome.CommentReplies,
        };
    }
}
