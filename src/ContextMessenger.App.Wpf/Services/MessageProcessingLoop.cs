using System.Text.Json;
using ContextMessenger.App.Wpf.Logging;
using ContextMessenger.App.Wpf.Patching;
using ContextMessenger.App.Wpf.Settings;
using ContextMessenger.Protocol;
using ContextMessenger.Protocol.Dispatch;
using Microsoft.Extensions.Logging;

namespace ContextMessenger.App.Wpf.Services;

public sealed class MessageProcessingLoop : IMessageProcessingLoop
{
    private readonly TargetProfile _target;
    private readonly RootProfile _root;
    private readonly IChatWindowWatcher _watcher;
    private readonly IRequestProcessor _processor;
    private readonly ITargetAutomationAdapter _automation;
    private readonly IProtocolLogFormatter _formatter;
    private readonly ILoggerFactory _loggerFactory;
    private readonly HeldPatchCoordinator? _patchCoordinator;
    private readonly Func<bool>? _isPatchReviewEnabled;
    private readonly SemaphoreSlim _processingGate = new(1, 1);
    private readonly HashSet<string> _seenRequestBodies = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seenRequestIds = new(StringComparer.Ordinal);
    private readonly object _activeProcessingSync = new();
    private CancellationTokenSource? _cts;
    private Task? _activeProcessingTask;
    private MessageProcessingLoopStatus _status = MessageProcessingLoopStatus.Stopped;
    private bool _disposed;
    private volatile bool _suppressCancellationResponse;
    private int _processingResourcesDisposed;

    public MessageProcessingLoop(
        TargetProfile target,
        RootProfile root,
        IChatWindowWatcher watcher,
        IRequestProcessor processor,
        ITargetAutomationAdapter automation,
        IProtocolLogFormatter formatter,
        ILoggerFactory loggerFactory,
        HeldPatchCoordinator? patchCoordinator = null,
        Func<bool>? isPatchReviewEnabled = null)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _watcher = watcher ?? throw new ArgumentNullException(nameof(watcher));
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _automation = automation ?? throw new ArgumentNullException(nameof(automation));
        _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _patchCoordinator = patchCoordinator;
        _isPatchReviewEnabled = isPatchReviewEnabled;
        _watcher.RequestDetected += OnRequestDetected;
    }

    /// <summary>Raised (on a background thread) when a patch outcome opened, updated, or closed the review page.</summary>
    public event EventHandler? PatchInteractionChanged;

    public bool IsRunning { get; private set; }

    public MessageProcessingLoopStatus Status
    {
        get => _status;
        private set
        {
            if (_status == value)
                return;

            _status = value;
            StatusChanged?.Invoke(this, value);
        }
    }

    public event EventHandler<LogEntry>? LogProduced;

    public event EventHandler<MessageProcessingLoopStatus>? StatusChanged;

    public void MarkRequestIdSeen(string requestId)
    {
        if (!string.IsNullOrWhiteSpace(requestId))
            _seenRequestIds.Add(requestId);
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsRunning)
            return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            IsRunning = _watcher.Start(_target);
            Status = IsRunning ? MessageProcessingLoopStatus.Idle : MessageProcessingLoopStatus.Error;

            Emit(
                IsRunning ? LogLevel.Information : LogLevel.Warning,
                IsRunning ? LogEntryKind.Automation : LogEntryKind.Warning,
                IsRunning
                    ? $"Auto-process started for {_target.Name} / {_root.Name}."
                    : $"{_target.Name} is not running; auto-process is idle.");
        }
        catch (Exception ex)
        {
            IsRunning = false;
            Status = MessageProcessingLoopStatus.Error;
            EmitError("Could not start auto-process.", ex);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!IsRunning && Status == MessageProcessingLoopStatus.Stopped)
            return;

        _cts?.Cancel();
        _watcher.Stop();
        IsRunning = false;
        Task? activeProcessing;
        lock (_activeProcessingSync)
            activeProcessing = _activeProcessingTask;

        if (activeProcessing is not null)
        {
            try
            {
                await activeProcessing;
            }
            catch (OperationCanceledException)
            {
            }
        }

        Status = MessageProcessingLoopStatus.Stopped;
        Emit(LogLevel.Information, LogEntryKind.Automation, $"Auto-process stopped for {_target.Name} / {_root.Name}.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _suppressCancellationResponse = true;
        _watcher.RequestDetected -= OnRequestDetected;
        _cts?.Cancel();
        _watcher.Stop();
        IsRunning = false;

        Task? activeProcessing;
        lock (_activeProcessingSync)
            activeProcessing = _activeProcessingTask;

        if (activeProcessing is null || activeProcessing.IsCompleted)
        {
            DisposeProcessingResources();
            return;
        }

        _ = activeProcessing.ContinueWith(
            _ => DisposeProcessingResources(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async void OnRequestDetected(object? sender, RequestDetectedEventArgs e)
    {
        var task = HandleRequestDetectedAsync(e.RequestBodies);
        lock (_activeProcessingSync)
            _activeProcessingTask = task;

        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            lock (_activeProcessingSync)
            {
                if (ReferenceEquals(_activeProcessingTask, task))
                    _activeProcessingTask = null;
            }
        }
    }

    private async Task HandleRequestDetectedAsync(IReadOnlyList<string> requestBodies)
    {
        var cancellationToken = _cts?.Token ?? CancellationToken.None;
        if (cancellationToken.IsCancellationRequested)
            return;

        await _processingGate.WaitAsync(cancellationToken);
        var shouldMarkSeen = false;
        IReadOnlyList<string> requests = [];
        try
        {
            requests = GetUnseenRequestBodies(requestBodies);
            if (requests.Count == 0)
                return;

            Status = MessageProcessingLoopStatus.Processing;
            Emit(
                LogLevel.Information,
                LogEntryKind.Info,
                $"Processing {requests.Count} new request block(s) from {requestBodies.Count} detected block(s).");
            Emit(LogLevel.Information, LogEntryKind.Request, _formatter.FormatRequestBodies(requests));

            var result = await Task.Run(
                () => _processor.ProcessRequestBodies(requests, cancellationToken));
            shouldMarkSeen = true;
            var output = result.ResponseText;
            if (string.IsNullOrEmpty(output))
            {
                Status = MessageProcessingLoopStatus.Idle;
                Emit(LogLevel.Information, LogEntryKind.Info, "All request IDs were duplicates; no response generated.");
                return;
            }

            foreach (var outcome in result.PatchOutcomes)
            {
                Emit(
                    LogLevel.Information,
                    LogEntryKind.Info,
                    $"Patch outcome: {outcome.CommandType} -> {outcome.PatchStatus} (patch {outcome.PatchId ?? "n/a"}, rev {outcome.Revision}).");
            }

            var (hold, interactionChanged) = EvaluatePatchReview(result, output);
            if (interactionChanged)
                PatchInteractionChanged?.Invoke(this, EventArgs.Empty);
            if (hold)
            {
                Status = MessageProcessingLoopStatus.Idle;
                Emit(LogLevel.Information, LogEntryKind.Info,
                    $"Patch held for manual review; response not sent to {_target.Name}.");
                return;
            }

            if (result.IsCancellationResponse && _suppressCancellationResponse)
                return;

            var deliveryToken = result.IsCancellationResponse
                ? CancellationToken.None
                : cancellationToken;
            var submitted = await _automation.SubmitResponseAsync(output, deliveryToken);
            if (submitted)
                Emit(LogLevel.Information, LogEntryKind.Response, _formatter.FormatResponse(output));

            Emit(
                submitted ? LogLevel.Information : LogLevel.Warning,
                submitted ? LogEntryKind.Automation : LogEntryKind.Warning,
                submitted
                    ? $"Response submitted to {_target.Name}."
                    : $"Response copied to clipboard, but could not submit to {_target.Name}.");
            Status = MessageProcessingLoopStatus.Idle;

            if (submitted && !result.IsCancellationResponse)
                _processor.OnResponseSubmitted();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Status = MessageProcessingLoopStatus.Error;
            EmitError("Process failed.", ex);
        }
        finally
        {
            if (shouldMarkSeen)
                MarkRequestBodiesSeen(requests);
            _watcher.Release();
            _processingGate.Release();
        }
    }

    // Returns whether the response should be held (suppress submit) and whether any patch
    // outcome was processed (so the host should refresh the review page open/close state).
    private (bool Hold, bool InteractionChanged) EvaluatePatchReview(ProcessRequestsResult result, string output)
    {
        if (_patchCoordinator is null || _isPatchReviewEnabled is null || result.PatchOutcomes.Count == 0)
            return (false, false);

        var holdEnabled = _isPatchReviewEnabled();
        var hold = false;
        foreach (var outcome in result.PatchOutcomes)
        {
            var decision = _patchCoordinator.Evaluate(new PatchHoldRequest
            {
                RootName = _root.Name,
                TargetName = _target.Name,
                ResponseText = output,
                Outcome = outcome,
                HoldEnabled = holdEnabled,
            });
            if (decision == PatchHoldDecision.Hold)
                hold = true;
        }
        return (hold, true);
    }

    private IReadOnlyList<string> GetUnseenRequestBodies(IReadOnlyList<string> requestBodies)
    {
        var unseen = new List<string>();

        foreach (var body in requestBodies)
        {
            var ids = TryGetRequestIds(body);
            if (ids.Count > 0)
            {
                if (ids.All(_seenRequestIds.Contains))
                    continue;
            }
            else if (_seenRequestBodies.Contains(body))
            {
                continue;
            }

            unseen.Add(body);
        }

        return unseen;
    }

    private void MarkRequestBodiesSeen(IReadOnlyList<string> requestBodies)
    {
        foreach (var body in requestBodies)
        {
            var ids = TryGetRequestIds(body);
            if (ids.Count > 0)
            {
                foreach (var id in ids)
                    _seenRequestIds.Add(id);
            }
            else
            {
                _seenRequestBodies.Add(body);
            }
        }
    }

    private void Emit(LogLevel level, LogEntryKind kind, string message)
    {
        LogProduced?.Invoke(this, new LogEntry
        {
            Timestamp = DateTimeOffset.Now,
            Level = level,
            Kind = kind,
            Message = message,
        });
    }

    private void EmitError(string message, Exception ex) =>
        Emit(LogLevel.Error, LogEntryKind.Error, $"{message}{Environment.NewLine}{ex}");

    private void DisposeProcessingResources()
    {
        if (Interlocked.Exchange(ref _processingResourcesDisposed, 1) != 0)
            return;

        _cts?.Dispose();
        _processingGate.Dispose();
        _watcher.Dispose();
        _loggerFactory.Dispose();
    }

    private static IReadOnlyList<string> TryGetRequestIds(string body)
    {
        try
        {
            return ProtocolParser.ParseBody(body)
                .Select(r => r.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToArray();
        }
        catch (ProtocolException)
        {
            return [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
