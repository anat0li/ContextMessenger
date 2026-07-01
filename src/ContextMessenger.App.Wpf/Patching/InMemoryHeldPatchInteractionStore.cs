namespace ContextMessenger.App.Wpf.Patching;

/// <summary>
/// Process-lifetime store for the active held interaction. The review service persists a
/// durable snapshot around this live slot so the coordinator can stay simple and testable.
/// </summary>
public sealed class InMemoryHeldPatchInteractionStore : IHeldPatchInteractionStore
{
    private readonly object _gate = new();
    private HeldPatchInteraction? _current;

    public event EventHandler? Changed;

    public HeldPatchInteraction? Current
    {
        get { lock (_gate) return _current; }
    }

    public void Save(HeldPatchInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(interaction);
        lock (_gate) _current = interaction;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_gate) _current = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
