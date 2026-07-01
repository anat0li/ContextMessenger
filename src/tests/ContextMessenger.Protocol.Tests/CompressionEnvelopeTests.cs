using System.Text.Json;
using ContextMessenger.Protocol;
using ContextMessenger.Protocol.Wire;

namespace ContextMessenger.Protocol.Tests;

public sealed class CompressionEnvelopeTests
{
    [Fact]
    public void Encode_decode_roundtrip_preserves_response()
    {
        var result = new ContextResponseResult { CommandIndex = 0, Type = "read_file" };
        result.Payload["content"] = JsonSerializer.SerializeToElement(new string('x', 1000));
        var response = new ContextResponse { Id = "abc", Results = [result] };

        var output = ProtocolWriter.Write(response, new ProtocolWriteOptions
        {
            CompressLargeResponses = true,
            CompressionThresholdBytes = 1,
        });

        var envelope = JsonSerializer.Deserialize<ContextResponseEnvelope>(ExtractBody(output))!;
        var decoded = ProtocolEnvelope.Decode(envelope);

        Assert.Equal("abc", decoded.Id);
        var decodedResult = Assert.Single(decoded.Results!);
        Assert.Equal("read_file", decodedResult.Type);
        Assert.Equal(new string('x', 1000), decodedResult.Payload["content"].GetString());
    }

    private static string ExtractBody(string output)
    {
        var start = output.IndexOf('\n') + 1;
        var end = output.LastIndexOf('\n');
        return output[start..end];
    }
}
