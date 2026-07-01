using System.Text;
using System.Text.Json;
using ContextMessenger.Protocol.Compression;
using ContextMessenger.Protocol.Wire;

namespace ContextMessenger.Protocol;

public static class ProtocolWriter
{
    public static string Write(ContextResponse response, ProtocolWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(response);
        return Write([ response ], options);
    }

    public static string Write(IReadOnlyList<ContextResponse> responses, ProtocolWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(responses);

        if (responses.Count == 0)
            return string.Empty;

        options ??= new ProtocolWriteOptions();

        var items = responses
            .Select(response => SerializeResponseOrEnvelope(response, options))
            .ToList();

        var json = responses.Count > 1 
            ? SerializeRawArray(items) 
            : items[0];

        var sb = new StringBuilder(json.Length + 64);
        sb.Append(ProtocolDelimiters.BeginResponse).Append('\n');
        sb.Append(json).Append('\n');
        sb.Append(ProtocolDelimiters.EndResponse);
        return sb.ToString();
    }

    public static string WriteError(string? id, string code, string message)
    {
        var response = new ContextResponse
        {
            Version = ProtocolValidator.CurrentVersion,
            Id = string.IsNullOrWhiteSpace(id) ? "unknown" : id,
            Status = ProtocolStatus.Error,
            ServerTimeUtc = ServerClock.NowIso8601Utc(),
            Error = new ContextResponseError { Code = code, Message = message },
        };
        return Write(response);
    }

    public static string WriteError(string? id, ProtocolException ex) =>
        WriteError(id, ex.Code, ex.Message);

    private static string SerializeResponseOrEnvelope(ContextResponse response, ProtocolWriteOptions options)
    {
        var responseJson = JsonSerializer.Serialize(response, JsonOptions.Indented);
        if (!options.CompressLargeResponses ||
            Encoding.UTF8.GetByteCount(responseJson) <= options.CompressionThresholdBytes)
        {
            return responseJson;
        }

        var envelope = new ContextResponseEnvelope
        {
            Id = response.Id,
            Payload = GzipBase64.Encode(responseJson),
        };

        return JsonSerializer.Serialize(envelope, JsonOptions.Indented);
    }

    private static string SerializeRawArray(IReadOnlyList<string> items)
    {
        if (items.Count == 0)
            return "[]";

        var sb = new StringBuilder();
        sb.AppendLine("[");
        for (var i = 0; i < items.Count; i++)
        {
            sb.Append(Indent(items[i], 2));
            if (i < items.Count - 1)
                sb.Append(',');
            sb.AppendLine();
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static string Indent(string value, int spaces)
    {
        var padding = new string(' ', spaces);
        return padding + value.Replace("\n", "\n" + padding, StringComparison.Ordinal);
    }
}
