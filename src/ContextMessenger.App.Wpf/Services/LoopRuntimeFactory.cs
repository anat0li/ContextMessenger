using ContextMessenger.App.Wpf.Logging;
using ContextMessenger.App.Wpf.Patching;
using ContextMessenger.App.Wpf.Settings;
using ContextMessenger.App.Wpf.ViewModels;
using ContextMessenger.Patching;
using Microsoft.Extensions.Logging;

namespace ContextMessenger.App.Wpf.Services;

/// <summary>A processing-loop runtime together with its patch-side handles.</summary>
public sealed record LoopRuntimeBundle(IMessageProcessingLoop Runtime, LoopPatchContext? Context);

/// <summary>
/// Builds the concrete runtime stack for one processing loop: logger factory, chat-window watcher,
/// protocol session, target automation, held-patch wiring, and the <see cref="MessageProcessingLoop"/>.
/// This is the composition seam that <see cref="LoopManager"/> depends on so the manager's
/// lifecycle bookkeeping can be unit-tested without real automation or git.
/// </summary>
public interface ILoopRuntimeFactory
{
    /// <param name="log">
    /// Sink for log entries produced by the runtime's logger provider and patch-response callback;
    /// the manager funnels these to its <see cref="LoopManager.LogProduced"/> event.
    /// </param>
    LoopRuntimeBundle Create(ProcessingLoopViewModel loop, Action<LogEntry> log);
}

public sealed class LoopRuntimeFactory : ILoopRuntimeFactory
{
    private readonly ISessionFactory _sessionFactory;
    private readonly Func<ILoggerFactory, IChatWindowWatcher> _watcherFactory;
    private readonly IClipboardService _clipboard;
    private readonly LoggingSettings _logging;
    private readonly PatchReviewService _reviewService;

    public LoopRuntimeFactory(
        ISessionFactory sessionFactory,
        Func<ILoggerFactory, IChatWindowWatcher> watcherFactory,
        IClipboardService clipboard,
        LoggingSettings logging,
        PatchReviewService reviewService)
    {
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        _watcherFactory = watcherFactory ?? throw new ArgumentNullException(nameof(watcherFactory));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _logging = logging ?? throw new ArgumentNullException(nameof(logging));
        _reviewService = reviewService ?? throw new ArgumentNullException(nameof(reviewService));
    }

    public LoopRuntimeBundle Create(ProcessingLoopViewModel loop, Action<LogEntry> log)
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddProvider(new UiLogProvider(log));
        });

        var watcher = _watcherFactory(loggerFactory);
        var session = _sessionFactory.Create(loop.Target, loop.Root);
        var processor = session.Processor;
        var automation = new WinAutomationTargetAutomationAdapter(
            loop.Target,
            _clipboard,
            loggerFactory.CreateLogger("WinAutomation"));
        var formatter = new ProtocolLogFormatter(_logging);

        HeldPatchCoordinator? coordinator = null;
        Func<bool>? isPatchReviewEnabled = null;
        LoopPatchContext? patchContext = null;
        if (session.Patches is not null)
        {
            // Keep the per-root hold-for-review flag and the patch service consistent.
            session.Patches.DeferAcceptanceByDefault = loop.IsPatchReviewEnabled;
            coordinator = new HeldPatchCoordinator(_reviewService.Store);
            var diffService = new LibGit2SharpPatchDiffService(loop.Root.Path);
            var actions = new HeldPatchActions(
                session.Patches,
                automation,
                _reviewService.Store,
                diffService,
                loop.Root.Path,
                _reviewService.RefreshProjection,
                response => log(new LogEntry
                {
                    Timestamp = DateTimeOffset.Now,
                    Level = LogLevel.Information,
                    Kind = LogEntryKind.Response,
                    Message = formatter.FormatResponse(response),
                }));
            patchContext = new LoopPatchContext(session.Patches, actions);
            isPatchReviewEnabled = () => loop.IsPatchReviewEnabled;
        }

        var patchLoop = new MessageProcessingLoop(
            loop.Target,
            loop.Root,
            watcher,
            processor,
            automation,
            formatter,
            loggerFactory,
            coordinator,
            isPatchReviewEnabled);

        return new LoopRuntimeBundle(patchLoop, patchContext);
    }
}
