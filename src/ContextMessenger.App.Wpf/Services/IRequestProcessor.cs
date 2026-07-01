using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.App.Wpf.Services;

public interface IRequestProcessor
{
    ProcessRequestsResult ProcessRequestBodies(
        IReadOnlyList<string> requests,
        CancellationToken cancellationToken = default);

    void OnResponseSubmitted() { }
}
