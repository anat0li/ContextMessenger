using System.Text.Json;
using ContextMessenger.Protocol.Compression;
using ContextMessenger.Protocol.Wire;

namespace ContextMessenger.Protocol;

public static class ProtocolEnvelope
{
    public static ContextResponse Decode(ContextResponseEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!string.Equals(envelope.Encoding, "gzip+base64", StringComparison.Ordinal))
            throw new ProtocolException(
                ProtocolErrorCodes.InvalidParameters,
                $"Unsupported response envelope encoding '{envelope.Encoding}'.");

        var json = GzipBase64.Decode(envelope.Payload);
        try
        {
            return JsonSerializer.Deserialize<ContextResponse>(json, JsonOptions.Strict)
                ?? throw new ProtocolException(
                    ProtocolErrorCodes.InvalidParameters,
                    "Decoded response envelope payload deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new ProtocolException(
                ProtocolErrorCodes.InvalidParameters,
                $"Decoded response envelope payload is not valid response JSON: {ex.Message}",
                ex);
        }
    }
}
