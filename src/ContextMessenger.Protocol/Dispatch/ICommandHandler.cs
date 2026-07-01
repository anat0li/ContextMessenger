using ContextMessenger.Protocol.Wire;

namespace ContextMessenger.Protocol.Dispatch;

public interface ICommandHandler
{
    string CommandType { get; }

    ContextResponseResult Execute(
        ContextCommand command,
        int commandIndex,
        CancellationToken cancellationToken = default);
}
