namespace ContextMessenger.Protocol.Dispatch;

/// <summary>
/// The structured outcome of a state-changing patch command (propose / amend / revert)
/// extracted from a processed batch, so the host can decide whether to hold the response
/// for review without re-parsing the serialized response block.
/// </summary>
public sealed record PatchOutcome
{
    /// <summary>The request id that produced this outcome, for correlating a later accept/revert response.</summary>
    public string RequestId { get; init; } = "";

    /// <summary>The patch command type that produced this outcome (propose_patch / amend_patch / revert_patch).</summary>
    public required string CommandType { get; init; }

    /// <summary>The resulting patch status (accepted / needs_revision / awaiting_acceptance / reverted / …).</summary>
    public required string PatchStatus { get; init; }

    public string? PatchId { get; init; }

    public int Revision { get; init; }

    /// <summary>Build-stage errors for this patch (empty when the build passed or was skipped).</summary>
    public IReadOnlyList<PatchBuildError> BuildErrors { get; init; } = [];

    /// <summary>Build-stage warnings for this patch (empty when none were reported).</summary>
    public IReadOnlyList<PatchBuildWarning> BuildWarnings { get; init; } = [];

    /// <summary>Compact status/count projection of the patch's last build stage.</summary>
    public PatchStageSummary BuildSummary { get; init; } = PatchStageSummary.Empty;

    /// <summary>Failed test cases for this patch (empty when tests passed or were skipped).</summary>
    public IReadOnlyList<PatchTestFailure> TestFailures { get; init; } = [];

    /// <summary>Compact status/count projection of the patch's last test stage.</summary>
    public PatchStageSummary TestSummary { get; init; } = PatchStageSummary.Empty;

    /// <summary>The model's review-thread messages, from an amend_patch's commentReplies.</summary>
    public IReadOnlyList<PatchCommentReply> CommentReplies { get; init; } = [];
}
