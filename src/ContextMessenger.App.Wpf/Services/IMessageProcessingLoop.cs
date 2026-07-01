using ContextMessenger.App.Wpf.Logging;

namespace ContextMessenger.App.Wpf.Services;

public interface IMessageProcessingLoop : IDisposable
{
    bool IsRunning { get; }

    MessageProcessingLoopStatus Status { get; }

    event EventHandler<LogEntry>? LogProduced;

    event EventHandler<MessageProcessingLoopStatus>? StatusChanged;

    event EventHandler? PatchInteractionChanged;

    void MarkRequestIdSeen(string requestId);

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync();
}
