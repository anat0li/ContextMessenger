namespace ContextMessenger.App.Wpf.Patching;

/// <summary>Direction of a single recorded exchange in a held patch's history.</summary>
public enum PatchInteractionDirection
{
    /// <summary>A message received from the model (propose, amend, answer).</summary>
    Inbound,

    /// <summary>A message sent to the model (needs-revision, reviewer comments, accepted, reverted).</summary>
    Outbound,
}

/// <summary>
/// One entry in a held patch's interaction timeline. Present for any active patch,
/// hold on or off — this is the "history of amend exchanges" the review page shows.
/// <see cref="Summary"/> carries the human-readable detail so the kind taxonomy can
/// stay coarse.
/// </summary>
public sealed record PatchInteractionEntry
{
    public required PatchInteractionDirection Direction { get; init; }

    public required string Summary { get; init; }

    public int Revision { get; init; }

    public DateTimeOffset AtUtc { get; init; } = DateTimeOffset.UtcNow;
}
