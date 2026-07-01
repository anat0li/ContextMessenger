using System.Text.Json;
using ContextMessenger.Protocol;
using ContextMessenger.Protocol.Wire;

namespace ContextMessenger.Protocol.Tests;

public sealed class ProtocolWriterTests
{
    [Fact]
    public void Write_wraps_response_with_delimiters()
    {
        var response = new ContextResponse
        {
            Id = "abc",
            Status = ProtocolStatus.Ok,
            Results = [],
        };

        var output = ProtocolWriter.Write(response);

        Assert.StartsWith(ProtocolDelimiters.BeginResponse, output);
        Assert.EndsWith(ProtocolDelimiters.EndResponse, output);
    }

    [Fact]
    public void Write_produces_indented_json()
    {
        var response = new ContextResponse { Id = "abc" };
        var output = ProtocolWriter.Write(response);
        Assert.Contains("\n  ", output);
    }

    [Fact]
    public void Write_omits_null_results_and_error()
    {
        var response = new ContextResponse { Id = "abc", Status = ProtocolStatus.Ok };
        var output = ProtocolWriter.Write(response);
        Assert.DoesNotContain("\"results\"", output);
        Assert.DoesNotContain("\"error\"", output);
    }

    [Fact]
    public void Write_includes_results_when_present()
    {
        var response = new ContextResponse
        {
            Id = "abc",
            Results = [new ContextResponseResult { CommandIndex = 0, Type = "tree" }],
        };
        var output = ProtocolWriter.Write(response);
        Assert.Contains("\"results\"", output);
        Assert.Contains("\"commandIndex\"", output);
        Assert.Contains("\"tree\"", output);
    }

    [Fact]
    public void Write_serializes_extension_payload_inline()
    {
        var result = new ContextResponseResult { CommandIndex = 0, Type = "tree" };
        result.Payload["content"] = JsonSerializer.SerializeToElement("src/\n  app/\n");

        var response = new ContextResponse { Id = "abc", Results = [result] };
        var output = ProtocolWriter.Write(response);

        Assert.Contains("\"content\"", output);
    }

    [Fact]
    public void Write_does_not_escape_csharp_signature_angle_brackets()
    {
        var result = new ContextResponseResult { CommandIndex = 0, Type = "find_symbol" };
        result.Payload["signature"] = JsonSerializer.SerializeToElement("public IReadOnlyList<ContextRequest> ParseBody(string input)");

        var response = new ContextResponse { Id = "abc", Results = [result] };
        var output = ProtocolWriter.Write(response);

        Assert.Contains("IReadOnlyList<ContextRequest>", output);
        Assert.DoesNotContain("\\u003C", output);
        Assert.DoesNotContain("\\u003E", output);
    }

    [Fact]
    public void Write_emits_object_form_when_batch_is_object_shape()
    {
        var output = ProtocolWriter.Write(new ContextResponse { Id = "abc" });

        var normalized = NormalizeNewlines(output);
        Assert.Contains("\n{\n", normalized);
        Assert.DoesNotContain("\n[\n", normalized);
    }

    [Fact]
    public void Write_emits_array_form_when_batch_is_array_shape()
    {
        var output = ProtocolWriter.Write([new ContextResponse { Id = "abc" }, new ContextResponse { Id = "def" }]);

        Assert.Contains("\n[\n", NormalizeNewlines(output));
        Assert.Contains("\"id\": \"abc\"", output);
        Assert.Contains("\"id\": \"def\"", output);
    }

    [Fact]
    public void Write_emits_empty_string_when_batch_has_no_responses()
    {
        var output = ProtocolWriter.Write([]);

        Assert.Equal("", output);
    }

    [Fact]
    public void Write_wraps_oversized_response_in_gzip_envelope_when_enabled()
    {
        var response = LargeResponse("abc");
        var output = ProtocolWriter.Write(response, new ProtocolWriteOptions
        {
            CompressLargeResponses = true,
            CompressionThresholdBytes = 100,
        });

        Assert.Contains("\"encoding\"", output);
        Assert.Contains("gzip", output);
        Assert.Contains("base64", output);
        Assert.Contains("\"payload\"", output);
    }

    [Fact]
    public void Write_does_not_wrap_when_compression_disabled()
    {
        var output = ProtocolWriter.Write(LargeResponse("abc"), new ProtocolWriteOptions
        {
            CompressLargeResponses = false,
            CompressionThresholdBytes = 1,
        });

        Assert.DoesNotContain("\"encoding\": \"gzip+base64\"", output);
        Assert.Contains("\"content\"", output);
    }

    [Fact]
    public void Write_does_not_wrap_below_threshold_even_when_enabled()
    {
        var output = ProtocolWriter.Write(new ContextResponse { Id = "abc" }, new ProtocolWriteOptions
        {
            CompressLargeResponses = true,
            CompressionThresholdBytes = 32_768,
        });

        Assert.DoesNotContain("\"encoding\": \"gzip+base64\"", output);
    }

    [Fact]
    public void WriteError_produces_structured_error_response()
    {
        var output = ProtocolWriter.WriteError("abc", ProtocolErrorCodes.InvalidJson, "bad json");

        Assert.Contains("\"id\": \"abc\"", output);
        Assert.Contains("\"status\": \"error\"", output);
        Assert.Contains($"\"code\": \"{ProtocolErrorCodes.InvalidJson}\"", output);
        Assert.Contains("\"message\": \"bad json\"", output);
    }

    [Fact]
    public void WriteError_uses_unknown_id_when_id_missing()
    {
        var output = ProtocolWriter.WriteError(null, ProtocolErrorCodes.InvalidJson, "missing");
        Assert.Contains("\"id\": \"unknown\"", output);
    }

    [Fact]
    public void WriteError_from_exception_carries_code_and_message()
    {
        var ex = new ProtocolException(ProtocolErrorCodes.InvalidVersion, "bad version");
        var output = ProtocolWriter.WriteError("abc", ex);

        Assert.Contains($"\"code\": \"{ProtocolErrorCodes.InvalidVersion}\"", output);
        Assert.Contains("\"message\": \"bad version\"", output);
    }

    [Fact]
    public void Write_then_parse_roundtrip_preserves_request_shape()
    {
        var request = new ContextRequest
        {
            Version = ProtocolValidator.CurrentVersion,
            Id = "11111111-1111-1111-1111-111111111111",
            Commands = [
                new ContextCommand { Type = "tree" },
                new ContextCommand { Type = "read_file" },
            ],
        };

        request.Commands[1].Parameters["path"] = JsonSerializer.SerializeToElement("src/foo.cs");

        var json = JsonSerializer.Serialize(request);

        var roundtripped = Assert.Single(ProtocolParser.ParseBody(json));
        Assert.Equal(request.Id, roundtripped.Id);
        Assert.Equal(2, roundtripped.Commands.Count);
        Assert.Equal("read_file", roundtripped.Commands[1].Type);
        Assert.Equal("src/foo.cs", roundtripped.Commands[1].Parameters["path"].GetString());
    }

    private static ContextResponse LargeResponse(string id)
    {
        var result = new ContextResponseResult { CommandIndex = 0, Type = "read_file" };
        result.Payload["content"] = JsonSerializer.SerializeToElement(new string('x', 1000));
        return new ContextResponse { Id = id, Results = [result] };
    }

    private static string NormalizeNewlines(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal);
}
