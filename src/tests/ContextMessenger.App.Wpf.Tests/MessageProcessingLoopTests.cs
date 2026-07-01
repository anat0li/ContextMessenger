using ContextMessenger.App.Wpf.Logging;
using ContextMessenger.App.Wpf.Patching;
using ContextMessenger.App.Wpf.Services;
using ContextMessenger.App.Wpf.Settings;
using ContextMessenger.Protocol.Dispatch;
using Microsoft.Extensions.Logging;

namespace ContextMessenger.App.Wpf.Tests;

public sealed class MessageProcessingLoopTests
{
    [Fact]
    public async Task Duplicate_request_id_is_processed_once()
    {
        var watcher = new FakeWatcher();
        var processor = new FakeProcessor(_ => ResponseFor("request-1"));
        var automation = new FakeAutomation();
        using var loop = CreateLoop(watcher, processor, automation);

        await loop.StartAsync();
        watcher.Raise(RequestFor("request-1"));
        await WaitForAsync(() => automation.SubmitCount == 1);

        watcher.Raise(RequestFor("request-1"));
        await Task.Delay(100);

        Assert.Equal(1, processor.CallCount);
        Assert.Equal(1, automation.SubmitCount);
        Assert.Equal(2, watcher.ReleaseCount);
    }

    [Fact]
    public async Task Seeded_request_id_is_ignored_without_processing()
    {
        var watcher = new FakeWatcher();
        var processor = new FakeProcessor(_ => ResponseFor("held-request"));
        var automation = new FakeAutomation();
        using var loop = CreateLoop(watcher, processor, automation);
        loop.MarkRequestIdSeen("held-request");

        await loop.StartAsync();
        watcher.Raise(RequestFor("held-request"));
        await Task.Delay(100);

        Assert.Equal(0, processor.CallCount);
        Assert.Equal(0, automation.SubmitCount);
        Assert.Equal(1, watcher.ReleaseCount);
    }

    [Fact]
    public async Task Unparseable_duplicate_body_is_processed_once_after_success()
    {
        var watcher = new FakeWatcher();
        var processor = new FakeProcessor(_ => ResponseFor("fallback"));
        var automation = new FakeAutomation();
        using var loop = CreateLoop(watcher, processor, automation);

        await loop.StartAsync();
        watcher.Raise("{not-json");
        await WaitForAsync(() => automation.SubmitCount == 1);

        watcher.Raise("{not-json");
        await Task.Delay(100);

        Assert.Equal(1, processor.CallCount);
        Assert.Equal(1, automation.SubmitCount);
        Assert.Equal(2, watcher.ReleaseCount);
    }

    [Fact]
    public async Task Failed_processing_does_not_mark_request_seen_so_retry_can_succeed()
    {
        var watcher = new FakeWatcher();
        var processor = new FakeProcessor(
            _ => throw new InvalidOperationException("boom"),
            _ => ResponseFor("retry"));
        var automation = new FakeAutomation();
        using var loop = CreateLoop(watcher, processor, automation);

        await loop.StartAsync();
        watcher.Raise(RequestFor("retry"));
        await WaitForAsync(() => loop.Status == MessageProcessingLoopStatus.Error);

        watcher.Raise(RequestFor("retry"));
        await WaitForAsync(() =>
            automation.SubmitCount == 1 &&
            loop.Status == MessageProcessingLoopStatus.Idle);

        Assert.Equal(2, processor.CallCount);
        Assert.Equal(1, automation.SubmitCount);
        Assert.Equal(MessageProcessingLoopStatus.Idle, loop.Status);
    }

    [Fact]
    public async Task Successful_submission_invokes_OnResponseSubmitted()
    {
        var watcher = new FakeWatcher();
        var processor = new FakeProcessor(_ => ResponseFor("submitted"));
        var automation = new FakeAutomation();
        using var loop = CreateLoop(watcher, processor, automation);

        await loop.StartAsync();
        watcher.Raise(RequestFor("submitted"));
        await WaitForAsync(() => processor.OnResponseSubmittedCount == 1);

        Assert.Equal(1, processor.OnResponseSubmittedCount);
    }

    [Fact]
    public async Task Failed_submission_does_not_invoke_OnResponseSubmitted()
    {
        var watcher = new FakeWatcher();
        var processor = new FakeProcessor(_ => ResponseFor("dropped"));
        var automation = new FakeAutomation { SubmitResult = false };
        using var loop = CreateLoop(watcher, processor, automation);

        await loop.StartAsync();
        watcher.Raise(RequestFor("dropped"));
        await WaitForAsync(() => automation.SubmitCount == 1);

        Assert.Equal(0, processor.OnResponseSubmittedCount);
    }

    [Fact]
    public async Task Failed_submission_does_not_log_response_block()
    {
        var watcher = new FakeWatcher();
        var processor = new FakeProcessor(_ => ResponseFor("not-sent"));
        var automation = new FakeAutomation { SubmitResult = false };
        using var loop = CreateLoop(watcher, processor, automation);
        var logs = new List<LogEntry>();
        loop.LogProduced += (_, entry) => logs.Add(entry);

        await loop.StartAsync();
        watcher.Raise(RequestFor("not-sent"));
        await WaitForAsync(() => automation.SubmitCount == 1);

        Assert.DoesNotContain(logs, entry => entry.Kind == LogEntryKind.Response);
        Assert.Contains(logs, entry => entry.Kind == LogEntryKind.Warning);
    }

    [Fact]
    public async Task Successful_pipeline_logs_request_response_and_submission()
    {
        var watcher = new FakeWatcher();
        var processor = new FakeProcessor(_ => ResponseFor("logged"));
        var automation = new FakeAutomation();
        using var loop = CreateLoop(watcher, processor, automation);
        var logs = new List<LogEntry>();
        loop.LogProduced += (_, entry) => logs.Add(entry);

        await loop.StartAsync();
        watcher.Raise(RequestFor("logged"));
        await WaitForAsync(() => automation.SubmitCount == 1);

        Assert.Contains(logs, entry => entry.Kind == LogEntryKind.Request);
        Assert.Contains(logs, entry => entry.Kind == LogEntryKind.Response);
        Assert.Contains(logs, entry => entry.Kind == LogEntryKind.Automation && entry.Message == "Response submitted to ChatGPT.");
    }

    [Fact]
    public async Task Held_patch_outcome_with_review_enabled_is_not_submitted()
    {
        var watcher = new FakeWatcher();
        var processor = new FakeProcessor(_ => ResponseFor("p-req"))
        {
            PatchOutcomes =
            [
                new PatchOutcome { RequestId = "p-req", CommandType = "propose_patch", PatchStatus = "needs_revision", PatchId = "p-1", Revision = 1 },
            ],
        };
        var automation = new FakeAutomation();
        var store = new InMemoryHeldPatchInteractionStore();
        var heldRaised = 0;
        using var loop = CreateLoop(watcher, processor, automation, new HeldPatchCoordinator(store), isReviewEnabled: true);
        loop.PatchInteractionChanged += (_, _) => heldRaised++;

        await loop.StartAsync();
        watcher.Raise(RequestFor("p-req"));
        await WaitForAsync(() => heldRaised == 1);

        Assert.Equal(0, automation.SubmitCount); // suppressed for review
        Assert.NotNull(store.Current);
        Assert.Equal("needs_revision", store.Current!.TransactionStatus);
        Assert.Equal("p-req", store.Current.RequestId);
        Assert.Equal("propose_patch", store.Current.CommandType);
    }

    [Fact]
    public async Task Patch_outcome_with_review_disabled_is_submitted_but_opens_review_page()
    {
        var watcher = new FakeWatcher();
        var processor = new FakeProcessor(_ => ResponseFor("p-req"))
        {
            PatchOutcomes =
            [
                new PatchOutcome { RequestId = "p-req", CommandType = "propose_patch", PatchStatus = "needs_revision", PatchId = "p-1", Revision = 1 },
            ],
        };
        var automation = new FakeAutomation();
        var store = new InMemoryHeldPatchInteractionStore();
        using var loop = CreateLoop(watcher, processor, automation, new HeldPatchCoordinator(store), isReviewEnabled: false);

        await loop.StartAsync();
        watcher.Raise(RequestFor("p-req"));
        await WaitForAsync(() => automation.SubmitCount == 1);

        // Review off: the response is delivered to the model, but the page-creation rule still
        // opens a review page (awaiting the model's reply) for the active patch.
        Assert.Equal(1, automation.SubmitCount); // delivered (review off)
        Assert.NotNull(store.Current);
        Assert.Equal("needs_revision", store.Current!.TransactionStatus);
        Assert.Equal(PatchInteractionPhase.AwaitingModelReply, store.Current.Phase);
    }

    [Fact]
    public async Task Stop_cancels_active_request_and_submits_cancellation_response()
    {
        var watcher = new FakeWatcher();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processor = new CancellingProcessor(started, cancelled);
        var automation = new FakeAutomation();
        using var loop = CreateLoop(watcher, processor, automation);

        await loop.StartAsync();
        watcher.Raise(RequestFor("cancel-me"));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));

        await loop.StopAsync();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(1, automation.SubmitCount);
        Assert.Contains("operation_cancelled", automation.LastText);
        Assert.False(automation.LastTokenWasCancelled);
        Assert.Equal(0, processor.OnResponseSubmittedCount);
    }

    [Fact]
    public async Task Dispose_cancels_active_request_without_submitting_response()
    {
        var watcher = new FakeWatcher();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processor = new CancellingProcessor(started, cancelled);
        var automation = new FakeAutomation();
        var loop = CreateLoop(watcher, processor, automation);

        await loop.StartAsync();
        watcher.Raise(RequestFor("cancel-on-dispose"));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));

        loop.Dispose();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Task.Delay(50);

        Assert.Equal(0, automation.SubmitCount);
        Assert.Equal(0, processor.OnResponseSubmittedCount);
    }

    private static MessageProcessingLoop CreateLoop(
        FakeWatcher watcher,
        IRequestProcessor processor,
        FakeAutomation automation)
    {
        var target = new TargetProfile { Name = "ChatGPT", ProcessName = "ChatGPT" };
        var root = new RootProfile { Name = "ContextMessenger", Path = "." };
        return new MessageProcessingLoop(
            target,
            root,
            watcher,
            processor,
            automation,
            new ProtocolLogFormatter(new LoggingSettings()),
            LoggerFactory.Create(_ => { }));
    }

    private static MessageProcessingLoop CreateLoop(
        FakeWatcher watcher,
        IRequestProcessor processor,
        FakeAutomation automation,
        HeldPatchCoordinator coordinator,
        bool isReviewEnabled)
    {
        var target = new TargetProfile { Name = "ChatGPT", ProcessName = "ChatGPT" };
        var root = new RootProfile { Name = "ContextMessenger", Path = "." };
        return new MessageProcessingLoop(
            target,
            root,
            watcher,
            processor,
            automation,
            new ProtocolLogFormatter(new LoggingSettings()),
            LoggerFactory.Create(_ => { }),
            coordinator,
            () => isReviewEnabled);
    }

    private static string RequestFor(string id) =>
        $$"""
        {
          "version": "1.0",
          "id": "{{id}}",
          "commands": [
            {
              "type": "tree",
              "path": ".",
              "depth": 1
            }
          ]
        }
        """;

    private static string ResponseFor(string id) =>
        $$"""
        BEGIN_RESPONSE
        {
          "version": "1.0.0.0",
          "id": "{{id}}",
          "status": "ok",
          "results": []
        }
        END_RESPONSE
        """;

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition())
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(10, cts.Token);
        }
    }

    private sealed class FakeWatcher : IChatWindowWatcher
    {
        public bool IsRunning { get; private set; }

        public int ReleaseCount { get; private set; }

        public event EventHandler<RequestDetectedEventArgs>? RequestDetected;

        public bool Start(TargetProfile target)
        {
            IsRunning = true;
            return true;
        }

        public void Stop()
        {
            IsRunning = false;
        }

        public void Release()
        {
            ReleaseCount++;
        }

        public void Dispose()
        {
        }

        public void Raise(params string[] requestBodies)
        {
            RequestDetected?.Invoke(this, new RequestDetectedEventArgs
            {
                RequestBodies = requestBodies,
            });
        }
    }

    private sealed class FakeProcessor : IRequestProcessor
    {
        private readonly Queue<Func<IReadOnlyList<string>, string>> _handlers;
        private readonly Func<IReadOnlyList<string>, string> _fallback;

        public FakeProcessor(params Func<IReadOnlyList<string>, string>[] handlers)
        {
            _handlers = new Queue<Func<IReadOnlyList<string>, string>>(handlers);
            _fallback = handlers.Last();
        }

        public int CallCount { get; private set; }

        public int OnResponseSubmittedCount { get; private set; }

        public IReadOnlyList<PatchOutcome> PatchOutcomes { get; set; } = [];

        public ProcessRequestsResult ProcessRequestBodies(
            IReadOnlyList<string> requests,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            var handler = _handlers.Count > 0 ? _handlers.Dequeue() : _fallback;
            return new ProcessRequestsResult { ResponseText = handler(requests), PatchOutcomes = PatchOutcomes };
        }

        public void OnResponseSubmitted() => OnResponseSubmittedCount++;
    }

    private sealed class FakeAutomation : ITargetAutomationAdapter
    {
        public int SubmitCount { get; private set; }

        public bool SubmitResult { get; set; } = true;

        public string LastText { get; private set; } = "";

        public bool LastTokenWasCancelled { get; private set; }

        public Task<bool> SubmitResponseAsync(string text, CancellationToken cancellationToken)
        {
            SubmitCount++;
            LastText = text;
            LastTokenWasCancelled = cancellationToken.IsCancellationRequested;
            return Task.FromResult(SubmitResult);
        }
    }

    private sealed class CancellingProcessor(
        TaskCompletionSource started,
        TaskCompletionSource cancelled) : IRequestProcessor
    {
        public int OnResponseSubmittedCount { get; private set; }

        public ProcessRequestsResult ProcessRequestBodies(
            IReadOnlyList<string> requests,
            CancellationToken cancellationToken = default)
        {
            started.TrySetResult();
            cancellationToken.WaitHandle.WaitOne();
            cancelled.TrySetResult();
            return new ProcessRequestsResult
            {
                ResponseText = """
                    BEGIN_RESPONSE
                    {
                      "version": "1.0",
                      "id": "cancel-me",
                      "status": "ok",
                      "results": [
                        {
                          "commandIndex": 0,
                          "type": "sql_query",
                          "status": "error",
                          "error": {
                            "code": "operation_cancelled",
                            "message": "The operation was cancelled by the user."
                          }
                        }
                      ]
                    }
                    END_RESPONSE
                    """,
                IsCancellationResponse = true,
            };
        }

        public void OnResponseSubmitted() => OnResponseSubmittedCount++;
    }
}
