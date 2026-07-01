using ContextMessenger.App.Wpf.Logging;
using ContextMessenger.App.Wpf.ViewModels;

namespace ContextMessenger.App.Wpf.Services;

/// <summary>
/// Owns the runtime lifecycle for every processing loop: creates and caches a
/// <see cref="IMessageProcessingLoop"/> (plus its <see cref="LoopPatchContext"/>) per loop via
/// <see cref="ILoopRuntimeFactory"/>, starts/stops/disposes them, and re-raises each runtime's
/// background events for the host view-model to project onto the UI. The host keeps the
/// VM-coupled reactions (status display, UI logging, review routing); this type keeps only the
/// registry and lifecycle bookkeeping so it can be unit-tested.
/// </summary>
public sealed class LoopManager : IDisposable
{
    private readonly ILoopRuntimeFactory _factory;
    private readonly Dictionary<ProcessingLoopViewModel, IMessageProcessingLoop> _runtimes = new();
    private readonly Dictionary<ProcessingLoopViewModel, LoopPatchContext> _contexts = new();

    public LoopManager(ILoopRuntimeFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>A runtime produced a log entry (from its logger provider, response callback, or itself).</summary>
    public event Action<ProcessingLoopViewModel, LogEntry>? LogProduced;

    /// <summary>A runtime's running/status state changed.</summary>
    public event Action<ProcessingLoopViewModel, IMessageProcessingLoop>? StatusChanged;

    /// <summary>A runtime reported a patch-interaction change (open/refresh/close the review).</summary>
    public event Action<ProcessingLoopViewModel>? PatchInteractionChanged;

    /// <summary>Returns the cached runtime for <paramref name="loop"/>, creating and wiring it on first use.</summary>
    public IMessageProcessingLoop GetOrCreate(ProcessingLoopViewModel loop)
    {
        if (_runtimes.TryGetValue(loop, out var existing))
            return existing;

        var bundle = _factory.Create(loop, entry => LogProduced?.Invoke(loop, entry));
        var runtime = bundle.Runtime;
        if (bundle.Context is not null)
            _contexts[loop] = bundle.Context;

        runtime.LogProduced += (_, entry) => LogProduced?.Invoke(loop, entry);
        runtime.StatusChanged += (_, _) => StatusChanged?.Invoke(loop, runtime);
        runtime.PatchInteractionChanged += (_, _) => PatchInteractionChanged?.Invoke(loop);

        _runtimes.Add(loop, runtime);
        return runtime;
    }

    public bool TryGetContext(ProcessingLoopViewModel loop, out LoopPatchContext context) =>
        _contexts.TryGetValue(loop, out context!);

    /// <summary>Creates the runtime if needed, starts it, and returns it.</summary>
    public async Task<IMessageProcessingLoop> StartAsync(ProcessingLoopViewModel loop)
    {
        var runtime = GetOrCreate(loop);
        await runtime.StartAsync();
        return runtime;
    }

    /// <summary>Stops the loop's runtime if one is cached; returns it, or null when nothing is running.</summary>
    public async Task<IMessageProcessingLoop?> StopAsync(ProcessingLoopViewModel loop)
    {
        if (!_runtimes.TryGetValue(loop, out var runtime))
            return null;

        await runtime.StopAsync();
        return runtime;
    }

    /// <summary>Disposes and forgets the loop's runtime; returns its patch context (or null if none existed).</summary>
    public LoopPatchContext? Remove(ProcessingLoopViewModel loop)
    {
        if (_runtimes.Remove(loop, out var runtime))
            runtime.Dispose();

        _contexts.Remove(loop, out var context);
        return context;
    }

    public void Dispose()
    {
        foreach (var runtime in _runtimes.Values)
            runtime.Dispose();

        _runtimes.Clear();
        _contexts.Clear();
    }
}
