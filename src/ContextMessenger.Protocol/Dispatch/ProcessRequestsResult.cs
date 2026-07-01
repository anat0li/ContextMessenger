namespace ContextMessenger.Protocol.Dispatch;

/// <summary>
/// Result of processing a batch of request bodies: the serialized response block to
/// deliver, plus any structured patch outcomes the batch produced. The text is what the
/// host submits today; the outcomes are the seam the hold-for-review coordinator consumes.
/// </summary>
public sealed record ProcessRequestsResult
{
    public required string ResponseText { get; init; }

    public IReadOnlyList<PatchOutcome> PatchOutcomes { get; init; } = [];

    public bool IsCancellationResponse { get; init; }
}
