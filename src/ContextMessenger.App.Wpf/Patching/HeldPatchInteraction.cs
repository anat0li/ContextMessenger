using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.App.Wpf.Patching;

/// <summary>
/// A patch whose model-facing response is being held for human review, projected for the
/// review page. Two orthogonal axes: <see cref="TransactionStatus"/> (the patch lifecycle
/// position) and <see cref="Phase"/> (whether the host is waiting on the model).
///
/// <para>
/// <see cref="HeldResponseText"/> is the <c>BEGIN_RESPONSE … END_RESPONSE</c> block that
/// would otherwise be sent to the chat target; it is empty when nothing is being held
/// (hold-off, where the review page still shows state and <see cref="History"/>).
/// </para>
/// </summary>
public sealed record HeldPatchInteraction
{
    public required string RootName { get; init; }

    public required string TargetName { get; init; }

    public required string PatchId { get; init; }

    /// <summary>The model request id this patch came from, used to correlate the response sent on accept/revert.</summary>
    public string RequestId { get; init; } = "";

    /// <summary>The patch command type that produced this patch (propose_patch / amend_patch).</summary>
    public string CommandType { get; init; } = "";

    public int Revision { get; init; }

    /// <summary>One of <see cref="PatchTransactionStatuses"/>.</summary>
    public required string TransactionStatus { get; init; }

    public PatchInteractionPhase Phase { get; init; } = PatchInteractionPhase.Reviewing;

    public string HeldResponseText { get; init; } = "";

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public IReadOnlyList<PatchInteractionEntry> History { get; init; } = [];

    /// <summary>Build-stage errors from the patch's last build (empty when the build passed/skipped).</summary>
    public IReadOnlyList<PatchBuildError> BuildErrors { get; init; } = [];

    /// <summary>Build-stage warnings from the patch's last build (empty when none were reported).</summary>
    public IReadOnlyList<PatchBuildWarning> BuildWarnings { get; init; } = [];

    /// <summary>Compact status/count projection of the patch's last build stage.</summary>
    public PatchStageSummary BuildSummary { get; init; } = PatchStageSummary.Empty;

    /// <summary>Failed test cases from the patch's last test run (empty when tests passed/skipped).</summary>
    public IReadOnlyList<PatchTestFailure> TestFailures { get; init; } = [];

    /// <summary>Compact status/count projection of the patch's last test stage.</summary>
    public PatchStageSummary TestSummary { get; init; } = PatchStageSummary.Empty;

    /// <summary>The model's replies to reviewer comments delivered with the last amend.</summary>
    public IReadOnlyList<PatchCommentReply> CommentReplies { get; init; } = [];

    /// <summary>
    /// Monotonic counter bumped on each processed outcome (amend/propose), used to apply model
    /// comment-replies exactly once even when a reply-only amend keeps the revision unchanged.
    /// </summary>
    public int ReplyTurn { get; init; }

    /// <summary>Returns a copy advanced to <paramref name="phase"/>, stamping <see cref="UpdatedAtUtc"/>.</summary>
    public HeldPatchInteraction WithPhase(PatchInteractionPhase phase) =>
        this with { Phase = phase, UpdatedAtUtc = DateTimeOffset.UtcNow };

    /// <summary>Returns a copy with <paramref name="status"/> and optional <paramref name="revision"/>, stamping <see cref="UpdatedAtUtc"/>.</summary>
    public HeldPatchInteraction WithStatus(string status, int? revision = null) =>
        this with
        {
            TransactionStatus = status,
            Revision = revision ?? Revision,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

    /// <summary>Returns a copy with <paramref name="entry"/> appended to <see cref="History"/>.</summary>
    public HeldPatchInteraction AppendHistory(PatchInteractionEntry entry) =>
        this with
        {
            History = [.. History, entry],
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
}
