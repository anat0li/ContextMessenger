namespace ContextMessenger.App.Wpf.Patching;

/// <summary>
/// Holds at most one active <see cref="HeldPatchInteraction"/>, mirroring the single
/// active-patch model the host already uses for patch session metadata. The store is
/// dumb storage; lifecycle/transition logic lives in the coordinator that drives it.
/// </summary>
public interface IHeldPatchInteractionStore
{
    /// <summary>The current held interaction, or null when none is active.</summary>
    HeldPatchInteraction? Current { get; }

    /// <summary>Stores (or replaces) the held interaction.</summary>
    void Save(HeldPatchInteraction interaction);

    /// <summary>Removes the held interaction, if any.</summary>
    void Clear();
}
