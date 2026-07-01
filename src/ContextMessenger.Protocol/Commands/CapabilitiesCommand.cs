using System.Text.Json.Serialization;
using ContextMessenger.Core.Meta;
using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.Protocol.Commands;

public sealed class CapabilitiesCommandParams
{
    [JsonPropertyName("command")]
    public string? Command { get; set; }
}

public sealed class CapabilitiesCommandResult
{
    [JsonPropertyName("commands")]
    public IReadOnlyList<CommandCapabilityInfo> Commands { get; set; } = [];
}

internal sealed class CapabilitiesHandler : CommandHandlerBase<CapabilitiesCommandParams, CapabilitiesCommandResult>
{
    private readonly HashSet<string>? _registeredCommands;

    public CapabilitiesHandler(IEnumerable<string>? registeredCommands = null)
    {
        _registeredCommands = registeredCommands is null
            ? null
            : new HashSet<string>(registeredCommands, StringComparer.OrdinalIgnoreCase);
    }

    public override string CommandType => CommandTypes.Capabilities;

    protected override CapabilitiesCommandResult ExecuteCore(CapabilitiesCommandParams parameters)
    {
        if (string.IsNullOrEmpty(parameters.Command))
        {
            var commands = _registeredCommands is null
                ? CommandCatalog.GetAll()
                : CommandCatalog.GetAll()
                    .Where(command => _registeredCommands.Contains(command.Name))
                    .ToArray();
            return new CapabilitiesCommandResult { Commands = commands };
        }

        var descriptor = CommandCatalog.Find(parameters.Command)
            ?? throw new ProtocolException(
                ProtocolErrorCodes.InvalidParameters,
                $"Unknown command '{parameters.Command}'.");
        if (_registeredCommands is not null && !_registeredCommands.Contains(descriptor.Name))
        {
            throw new ProtocolException(
                ProtocolErrorCodes.InvalidParameters,
                $"Command '{parameters.Command}' is not available for the active root.");
        }

        return new CapabilitiesCommandResult { Commands = [descriptor] };
    }
}
