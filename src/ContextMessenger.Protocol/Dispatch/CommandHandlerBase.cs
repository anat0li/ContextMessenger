using System.Text.Json;
using ContextMessenger.Protocol.Wire;

namespace ContextMessenger.Protocol.Dispatch;

public abstract class CommandHandlerBase<TParams, TResult> : ICommandHandler
    where TParams : new()
{
    public abstract string CommandType { get; }

    public ContextResponseResult Execute(
        ContextCommand command,
        int commandIndex,
        CancellationToken cancellationToken = default)
    {
        var parameters = DeserializeParams(command.Parameters);
        cancellationToken.ThrowIfCancellationRequested();
        var result = ExecuteCore(parameters, cancellationToken);
        return new ContextResponseResult
        {
            CommandIndex = commandIndex,
            Type = CommandType,
            Status = ProtocolStatus.Ok,
            Payload = SerializeResult(result),
        };
    }

    protected virtual TResult ExecuteCore(TParams parameters) =>
        throw new NotSupportedException($"{GetType().Name} must implement command execution.");

    protected virtual TResult ExecuteCore(TParams parameters, CancellationToken cancellationToken) =>
        ExecuteCore(parameters);

    private static TParams DeserializeParams(IReadOnlyDictionary<string, JsonElement> parameters)
    {
        if (parameters.Count == 0) return new TParams();
        try
        {
            var json = JsonSerializer.Serialize(parameters);
            return JsonSerializer.Deserialize<TParams>(json, JsonOptions.Strict) ?? new TParams();
        }
        catch (JsonException ex)
        {
            throw new ProtocolException(
                ProtocolErrorCodes.InvalidParameters,
                $"Could not bind command parameters: {ex.Message}",
                ex);
        }
    }

    private static Dictionary<string, JsonElement> SerializeResult(TResult result)
    {
        var element = JsonSerializer.SerializeToElement(result, JsonOptions.Strict);
        var dict = new Dictionary<string, JsonElement>();
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
                dict[prop.Name] = prop.Value.Clone();
        }
        return dict;
    }
}
