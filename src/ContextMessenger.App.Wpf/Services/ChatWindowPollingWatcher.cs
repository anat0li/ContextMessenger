using ContextMessenger.App.Wpf.Services;
using ContextMessenger.App.Wpf.Settings;
using Microsoft.Extensions.Logging;

public sealed class ChatWindowPollingWatcher : IChatWindowWatcher
{
    private const int DelayMs = 2000;
    private const int TimeoutMs = 20000;

    private readonly AutoResetEvent _event = new(false);
    private readonly ILogger<ChatWindowPollingWatcher> _logger;
    private CancellationTokenSource? _cts;
    private Task? _worker;
    private TargetProfile? _target;

    public ChatWindowPollingWatcher(IChatScanner scanner, ILogger<ChatWindowPollingWatcher> logger)
    {
        ChatScanner = scanner;
        _logger = logger;
    }

    public bool IsRunning => _worker is { IsCompleted: false };

    public event EventHandler<RequestDetectedEventArgs>? RequestDetected;

    public IChatScanner ChatScanner { get; }

    public bool Start(TargetProfile target)
    {
        Stop();

        ArgumentNullException.ThrowIfNull(target);

        if (WinAutomation.FindMainProcess(target.ProcessName) == null)
            return false;

        _target = target;
        _cts = new CancellationTokenSource();
        _worker = Task.Run(() => WatchLoopAsync(_cts.Token));

        _logger.LogInformation("Started watcher for '{TargetName}' ({ProcessName}).", target.Name, target.ProcessName);
        return true;
    }

    public void Release()
    {
        _event.Set();
    }

    private async Task WatchLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_target is not null)
            {
                try
                {
                    var bodies = ChatScanner.ScanForRequestBodies(_target);
                    if (bodies.Count > 0)
                    {
                        RequestDetected?.Invoke(this, new RequestDetectedEventArgs
                        {
                            RequestBodies = bodies,
                            DetectedAt = DateTime.UtcNow,
                        });
                        _event.WaitOne(TimeoutMs);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Scan error.");
                }
            }

            await Task.Delay(DelayMs, ct);
        }
    }

    public void Stop()
    {
        var hadWorker = _worker is not null || _cts is not null;
        _cts?.Cancel();

        try { _worker?.Wait(1000); }
        catch { }

        _cts?.Dispose();
        _cts = null;
        _worker = null;
        _target = null;

        if (hadWorker)
            _logger.LogInformation("Stopped watcher.");
    }

    public void Dispose() => Stop();
}
