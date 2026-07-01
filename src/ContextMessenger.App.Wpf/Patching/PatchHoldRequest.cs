using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.App.Wpf.Patching;

/// <summary>
/// Inputs the <see cref="HeldPatchCoordinator"/> needs to decide hold-vs-deliver for a
/// processed response that produced a patch outcome.
/// </summary>
public sealed record PatchHoldRequest
{
    public required string RootName { get; init; }

    public required string TargetName { get; init; }

    /// <summary>The serialized response block that would otherwise be submitted.</summary>
    public required string ResponseText { get; init; }

    public required PatchOutcome Outcome { get; init; }

    /// <summary>Whether hold-for-review is enabled for this root (latched at outcome time).</summary>
    public required bool HoldEnabled { get; init; }
}
