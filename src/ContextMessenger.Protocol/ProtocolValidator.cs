using ContextMessenger.Protocol.Wire;
using System.Reflection;

namespace ContextMessenger.Protocol;

public static class ProtocolValidator
{
    public static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version!;

    public static void Validate(IReadOnlyList<ContextRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);

        if (requests.Count == 0)
            throw new ProtocolException(
                ProtocolErrorCodes.EmptyBatch,
                "Request batch must contain at least one request.");

        foreach (var request in requests)
            Validate(request);
    }

    public static void Validate(ContextRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Version > CurrentVersion 
            || request.Version.Major != CurrentVersion.Major)
            throw new ProtocolException(
                ProtocolErrorCodes.InvalidVersion,
                $"Unsupported protocol version: {request.Version}. Expected {CurrentVersion.Major}.0 or greater.");

        if (string.IsNullOrWhiteSpace(request.Id))
            throw new ProtocolException(
                ProtocolErrorCodes.MissingId,
                "Request 'id' is required and must be a non-empty string.");

        if (request.Commands is null)
            throw new ProtocolException(
                ProtocolErrorCodes.MissingCommands,
                "Request 'commands' field is required.");

        if (request.Commands.Count == 0)
            throw new ProtocolException(
                ProtocolErrorCodes.EmptyCommandSet,
                "Request 'commands' must contain at least one command.");

        for (var i = 0; i < request.Commands.Count; i++)
        {
            var cmd = request.Commands[i];
            if (cmd is null || string.IsNullOrWhiteSpace(cmd.Type))
                throw new ProtocolException(
                    ProtocolErrorCodes.MissingCommandType,
                    $"Command at index {i} is missing 'type'.");
        }
    }
}
