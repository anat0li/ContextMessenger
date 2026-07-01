using System.Text.Json;
using ContextMessenger.Core.Patching;
using ContextMessenger.Protocol.Commands;
using ContextMessenger.Protocol.Wire;

namespace ContextMessenger.Protocol;

/// <summary>
/// Builds a correlated <c>BEGIN_RESPONSE … END_RESPONSE</c> block from a
/// <see cref="PatchTransactionResult"/> produced by a direct service call (accept/revert).
/// The host uses this to send the outcome of a reviewed patch back to the model under the
/// patch's original request id, the same shape the dispatcher would have produced.
/// </summary>
public static class PatchResponseBuilder
{
    public static string Build(
        string requestId,
        string commandType,
        int commandIndex,
        PatchTransactionResult result,
        ProtocolWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        var response = new ContextResponse
        {
            Version = ProtocolValidator.CurrentVersion,
            Id = string.IsNullOrEmpty(requestId) ? "unknown" : requestId,
            Status = ProtocolStatus.Ok,
            ServerTimeUtc = ServerClock.NowIso8601Utc(),
            Results =
            [
                new ContextResponseResult
                {
                    CommandIndex = commandIndex,
                    Type = string.IsNullOrEmpty(commandType) ? CommandTypes.ProposePatch : commandType,
                    Status = ProtocolStatus.Ok,
                    Payload = ToPayload(PatchTransactionCommandResult.FromCore(result)),
                },
            ],
        };

        return ProtocolWriter.Write(response, options);
    }

    private static Dictionary<string, JsonElement> ToPayload(PatchTransactionCommandResult result)
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
