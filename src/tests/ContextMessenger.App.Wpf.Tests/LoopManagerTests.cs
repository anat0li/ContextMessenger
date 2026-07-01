using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ContextMessenger.App.Wpf.Logging;
using ContextMessenger.App.Wpf.Patching;
using ContextMessenger.App.Wpf.Services;
using ContextMessenger.App.Wpf.Settings;
using ContextMessenger.App.Wpf.ViewModels;
using ContextMessenger.Core.Patching;
using ContextMessenger.Protocol.Review;
using Xunit;

namespace ContextMessenger.App.Wpf.Tests;

public sealed class LoopManagerTests
{
    private static ProcessingLoopViewModel Loop(string root = "Repo") =>
        new(new TargetProfile { Name = "ChatGPT", ProcessName = "ChatGPT" },
            new RootProfile { Name = root, Path = "C:/repo" },
            new LoggingSettings());

    [Fact]
    public void GetOrCreate_caches_runtime_and_context_per_loop()
    {
        var factory = new FakeLoopRuntimeFactory();
        using var manager = new LoopManager(factory);
        var loop = Loop();

        var first = manager.GetOrCreate(loop);
        var second = manager.GetOrCreate(loop);

        Assert.Same(first, second);
        Assert.Equal(1, factory.CreateCount);
        Assert.True(manager.TryGetContext(loop, out var context));
        Assert.Same(factory.Created[0].Context, context);
    }

    [Fact]
    public void GetOrCreate_creates_distinct_runtimes_for_distinct_loops()
    {
        var factory = new FakeLoopRuntimeFactory();
        using var manager = new LoopManager(factory);

        var a = manager.GetOrCreate(Loop("A"));
        var b = manager.GetOrCreate(Loop("B"));

        Assert.NotSame(a, b);
        Assert.Equal(2, factory.CreateCount);
    }

    [Fact]
    public void Runtime_events_are_forwarded_with_the_owning_loop()
    {
        var factory = new FakeLoopRuntimeFactory();
        using var manager = new LoopManager(factory);
        var loop = Loop();

        ProcessingLoopViewModel? logLoop = null;
        LogEntry? logged = null;
        ProcessingLoopViewModel? statusLoop = null;
        IMessageProcessingLoop? statusRuntime = null;
        ProcessingLoopViewModel? interactionLoop = null;

        manager.LogProduced += (l, e) => { logLoop = l; logged = e; };
        manager.StatusChanged += (l, r) => { statusLoop = l; statusRuntime = r; };
        manager.PatchInteractionChanged += l => interactionLoop = l;

        var runtime = (FakeMessageProcessingLoop)manager.GetOrCreate(loop);
        var entry = new LogEntry { Message = "hi", Kind = LogEntryKind.Info };
        runtime.RaiseLog(entry);
        runtime.RaiseStatusChanged();
        runtime.RaisePatchInteractionChanged();

        Assert.Same(loop, logLoop);
        Assert.Same(entry, logged);
        Assert.Same(loop, statusLoop);
        Assert.Same(runtime, statusRuntime);
        Assert.Same(loop, interactionLoop);
    }

    [Fact]
    public void Factory_log_callback_is_forwarded_to_LogProduced()
    {
        var factory = new FakeLoopRuntimeFactory();
        using var manager = new LoopManager(factory);
        var loop = Loop();

        ProcessingLoopViewModel? logLoop = null;
        LogEntry? logged = null;
        manager.LogProduced += (l, e) => { logLoop = l; logged = e; };

        manager.GetOrCreate(loop);
        var entry = new LogEntry { Message = "from-provider", Kind = LogEntryKind.Response };
        factory.Created[0].Log(entry);

        Assert.Same(loop, logLoop);
        Assert.Same(entry, logged);
    }

    [Fact]
    public async Task StartAsync_creates_starts_and_returns_runtime()
    {
        var factory = new FakeLoopRuntimeFactory();
        using var manager = new LoopManager(factory);
        var loop = Loop();

        var runtime = (FakeMessageProcessingLoop)await manager.StartAsync(loop);

        Assert.Equal(1, runtime.StartCount);
        Assert.True(runtime.IsRunning);
        Assert.Same(runtime, manager.GetOrCreate(loop));
    }

    [Fact]
    public async Task StopAsync_returns_null_when_no_runtime_cached()
    {
        var factory = new FakeLoopRuntimeFactory();
        using var manager = new LoopManager(factory);

        var result = await manager.StopAsync(Loop());

        Assert.Null(result);
        Assert.Equal(0, factory.CreateCount);
    }

    [Fact]
    public async Task StopAsync_stops_existing_runtime()
    {
        var factory = new FakeLoopRuntimeFactory();
        using var manager = new LoopManager(factory);
        var loop = Loop();
        var created = (FakeMessageProcessingLoop)manager.GetOrCreate(loop);

        var stopped = (FakeMessageProcessingLoop?)await manager.StopAsync(loop);

        Assert.Same(created, stopped);
        Assert.Equal(1, created.StopCount);
    }

    [Fact]
    public void Remove_disposes_runtime_returns_context_and_forgets_loop()
    {
        var factory = new FakeLoopRuntimeFactory();
        using var manager = new LoopManager(factory);
        var loop = Loop();
        var runtime = (FakeMessageProcessingLoop)manager.GetOrCreate(loop);
        var context = factory.Created[0].Context;

        var removed = manager.Remove(loop);

        Assert.Same(context, removed);
        Assert.Equal(1, runtime.DisposeCount);
        Assert.False(manager.TryGetContext(loop, out _));
    }

    [Fact]
    public void Remove_unknown_loop_returns_null()
    {
        var factory = new FakeLoopRuntimeFactory();
        using var manager = new LoopManager(factory);

        Assert.Null(manager.Remove(Loop()));
    }

    [Fact]
    public void Dispose_disposes_all_runtimes_and_clears_registries()
    {
        var factory = new FakeLoopRuntimeFactory();
        var manager = new LoopManager(factory);
        var loopA = Loop("A");
        var loopB = Loop("B");
        var a = (FakeMessageProcessingLoop)manager.GetOrCreate(loopA);
        var b = (FakeMessageProcessingLoop)manager.GetOrCreate(loopB);

        manager.Dispose();

        Assert.Equal(1, a.DisposeCount);
        Assert.Equal(1, b.DisposeCount);
        Assert.False(manager.TryGetContext(loopA, out _));
        Assert.False(manager.TryGetContext(loopB, out _));
    }

    // --- fakes ---

    private sealed class FakeLoopRuntimeFactory : ILoopRuntimeFactory
    {
        public List<CreatedRuntime> Created { get; } = new();
        public int CreateCount => Created.Count;

        public LoopRuntimeBundle Create(ProcessingLoopViewModel loop, Action<LogEntry> log)
        {
            var runtime = new FakeMessageProcessingLoop();
            var context = new LoopPatchContext(new StubPatches(), new StubActions());
            Created.Add(new CreatedRuntime(loop, runtime, context, log));
            return new LoopRuntimeBundle(runtime, context);
        }
    }

    private sealed record CreatedRuntime(
        ProcessingLoopViewModel Loop,
        FakeMessageProcessingLoop Runtime,
        LoopPatchContext? Context,
        Action<LogEntry> Log);

    private sealed class FakeMessageProcessingLoop : IMessageProcessingLoop
    {
        public bool IsRunning { get; private set; }
        public MessageProcessingLoopStatus Status { get; set; } = MessageProcessingLoopStatus.Idle;
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public int DisposeCount { get; private set; }

        public event EventHandler<LogEntry>? LogProduced;
        public event EventHandler<MessageProcessingLoopStatus>? StatusChanged;
        public event EventHandler? PatchInteractionChanged;

        public void MarkRequestIdSeen(string requestId) { }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            StartCount++;
            IsRunning = true;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            StopCount++;
            IsRunning = false;
            return Task.CompletedTask;
        }

        public void Dispose() => DisposeCount++;

        public void RaiseLog(LogEntry entry) => LogProduced?.Invoke(this, entry);
        public void RaiseStatusChanged() => StatusChanged?.Invoke(this, Status);
        public void RaisePatchInteractionChanged() => PatchInteractionChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class StubPatches : IPatchTransactionService
    {
        public bool HasActivePatch => false;
        public bool DeferAcceptanceByDefault { get; set; }
        public PatchTransactionResult Propose(ProposePatchRequest request) => throw new NotSupportedException();
        public PatchTransactionResult Amend(AmendPatchRequest request) => throw new NotSupportedException();
        public PatchValidationResult Validate(ValidatePatchRequest request) => throw new NotSupportedException();
        public PatchTransactionResult Accept(string patchId) => throw new NotSupportedException();
        public PatchTransactionResult Revert(string patchId) => throw new NotSupportedException();
        public PatchTransactionResult Current() => throw new NotSupportedException();
    }

    private sealed class StubActions : IHeldPatchActions
    {
        public Task SendAsync(IReadOnlyList<ReviewerComment> comments) => throw new NotSupportedException();
        public Task AcceptAsync() => throw new NotSupportedException();
        public Task RevertAsync() => throw new NotSupportedException();
        public Task RefreshAsync() => throw new NotSupportedException();
        public PatchReviewSnapshot GetSnapshot() => throw new NotSupportedException();
        public string? GetFileDiff(string path) => throw new NotSupportedException();
        public string? GetFileContent(string path) => throw new NotSupportedException();
    }
}
