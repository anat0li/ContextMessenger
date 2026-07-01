using ContextMessenger.App.Wpf.Settings;

namespace ContextMessenger.App.Wpf.Services;

public sealed class RequestDetectedEventArgs : EventArgs
{
    public IReadOnlyList<string> RequestBodies { get; init; } = [];
    public DateTime DetectedAt { get; init; } = DateTime.UtcNow;
}

public interface IChatWindowWatcher : IDisposable
{
    bool IsRunning { get; }

    event EventHandler<RequestDetectedEventArgs>? RequestDetected;

    /// <summary>
    /// Attach to the visible window of the target process and start watching
    /// for configured request blocks below the most recent user prompt.
    /// </summary>
    /// <returns>
    /// <c>true</c> if attach succeeded and the watcher is now running;
    /// <c>false</c> if the process or its main window could not be located
    /// (typically because the target app isn't running).
    /// </returns>
    bool Start(TargetProfile target);

    void Stop();
    void Release();
}
