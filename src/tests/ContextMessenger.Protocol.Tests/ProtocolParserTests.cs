namespace ContextMessenger.Protocol.Tests;

public sealed class ProtocolParserTests
{
    private const string ValidBlock = """
        {
          "version": "1.0.0.0",
          "id": "6f77b950-9e4d-4c6b-9bc7-8db7502c1a3e",
          "commands": [
            { "type": "tree", "path": ".", "depth": 2 }
          ]
        }
        """;

    [Fact]
    public void Parse_accepts_single_request_object_form()
    {
        var batch = ProtocolParser.ParseBody(ValidBlock);
        var request = Assert.Single(batch);

        Assert.Equal(ProtocolValidator.CurrentVersion, request.Version);
        Assert.Equal("6f77b950-9e4d-4c6b-9bc7-8db7502c1a3e", request.Id);
        Assert.Single(request.Commands);
        Assert.Equal("tree", request.Commands[0].Type);
    }

    [Fact]
    public void Parse_captures_command_extension_data()
    {
        var request = Assert.Single(ProtocolParser.ParseBody(ValidBlock));
        var cmd = request.Commands[0];

        Assert.True(cmd.Parameters.ContainsKey("path"));
        Assert.True(cmd.Parameters.ContainsKey("depth"));
        Assert.Equal(".", cmd.Parameters["path"].GetString());
        Assert.Equal(2, cmd.Parameters["depth"].GetInt32());
    }

    [Fact]
    public void Parse_accepts_multi_request_array_form()
    {
        var input = """
            [
              { "id": "11111111-1111-1111-1111-111111111111", "commands": [{ "type": "tree" }] },
              { "id": "22222222-2222-2222-2222-222222222222", "commands": [{ "type": "read_file", "path": "src/foo.cs" }] }
            ]
            """;

        var batch = ProtocolParser.ParseBody(input);

        Assert.Equal(2, batch.Count);
        Assert.Equal("11111111-1111-1111-1111-111111111111", batch[0].Id);
        Assert.Equal("22222222-2222-2222-2222-222222222222", batch[1].Id);
    }

    [Fact]
    public void Parse_records_array_shape_in_batch()
    {
        var input = """
            [{ "id": "x", "commands": [{ "type": "tree" }] }]
            """;

        Assert.Single(ProtocolParser.ParseBody(input));
    }

    [Fact]
    public void Parse_records_object_shape_in_batch()
    {
        Assert.Single(ProtocolParser.ParseBody(ValidBlock));
    }

    [Fact]
    public void Parse_handles_missing_version_as_default_one()
    {
        var input = """
            { "id": "x", "commands": [{ "type": "tree" }] }
            """;

        var request = Assert.Single(ProtocolParser.ParseBodyAndValidate(input));
        Assert.Equal(ProtocolValidator.CurrentVersion, request.Version);
    }

    [Fact]
    public void Parse_handles_compact_missing_version_request()
    {
        var input = """{"id":"a82b14e6-7c3f-49d0-bf21-5d6e8a0c1f47","commands":[{"type":"tree","path":".","depth":2}]}""";

        var request = Assert.Single(ProtocolParser.ParseBodyAndValidate(input));

        Assert.Equal(ProtocolValidator.CurrentVersion, request.Version);
        Assert.Equal("a82b14e6-7c3f-49d0-bf21-5d6e8a0c1f47", request.Id);
        Assert.Single(request.Commands);
    }

    [Fact]
    public void Parse_trims_body_before_shape_check()
    {
        var input = """

              {"id":"a82b14e6-7c3f-49d0-bf21-5d6e8a0c1f47","commands":[{"type":"tree"}]}

            """;

        var request = Assert.Single(ProtocolParser.ParseBodyAndValidate(input));

        Assert.Equal("a82b14e6-7c3f-49d0-bf21-5d6e8a0c1f47", request.Id);
    }

    [Fact]
    public void Parse_repairs_rendered_line_breaks_inside_json_string()
    {
        var input = @"
[
{
""version"": ""1.0"",
""id"": ""091a2b3c-4d5e-417f-0182-93041526374a"",
""commands"": [
{ ""type"": ""find_symbol"", ""name"": ""ProtocolParser"" }
]
},
{
""version"": ""1.0"",
""id"": ""1a2b3c4d-5e6f-4180-1293-04152637485b"",
""commands"": [
{ ""type"": ""find_symbol"", ""name"": ""Protocol"", ""match"": ""prefix"", ""maxResults"": 20 }
]
},
{
""version"": ""1.0"",
""id"": ""2b3c4d5e-6f70-4191-23a4-152637485c6d"",
""commands"": [
{ ""type"": ""find_references"", ""symbolId"": ""T
:ContextMessenger
.Protocol.ProtocolParser"" }
]
},
{
""version"": ""1.0"",
""id"": ""3c4d5e6f-7081-41a2-34b5-2637485c6d7e"",
""commands"": [
{ ""type"": ""goto_definition"", ""path"": ""src/ContextMessenger.Protocol/ProtocolParser.cs"", ""line"": 8, ""column"": 38 }
]
}
]
            ";
        var requests = ProtocolParser.ParseBodyAndValidate(input);

        Assert.Equal(4, requests.Count);
        var command = Assert.Single(requests[2].Commands);
        Assert.Equal("find_references", command.Type);
        Assert.Equal(
            "T:ContextMessenger.Protocol.ProtocolParser",
            command.Parameters["symbolId"].GetString());
    }

    [Fact]
    public void Parse_rejects_explicit_version_two()
    {
        var input = """
            { "version": "2.0", "id": "x", "commands": [{ "type": "tree" }] }
            """;

        var ex = Assert.Throws<ProtocolException>(() => ProtocolParser.ParseBodyAndValidate(input));
        Assert.Equal(ProtocolErrorCodes.InvalidVersion, ex.Code);
    }

    [Fact]
    public void Parse_throws_EmptyBatch_for_empty_array()
    {
        var input = """
            []
            """;

        var ex = Assert.Throws<ProtocolException>(() => ProtocolParser.ParseBodyAndValidate(input));
        Assert.Equal(ProtocolErrorCodes.EmptyBatch, ex.Code);
    }

    [Fact]
    public void Parse_throws_InvalidJson_when_input_empty()
    {
        var ex = Assert.Throws<ProtocolException>(() => ProtocolParser.ParseBody(""));
        Assert.Equal(ProtocolErrorCodes.InvalidJson, ex.Code);
    }

    [Fact]
    public void Parse_throws_InvalidJson_when_no_delimiters_present()
    {
        var ex = Assert.Throws<ProtocolException>(() =>
            ProtocolParser.ParseBody("just some random chatter without delimiters"));
        Assert.Equal(ProtocolErrorCodes.InvalidJson, ex.Code);
    }

    [Fact]
    public void Parse_throws_InvalidJson_for_malformed_payload()
    {
        var input = """
            { not valid json }
            """;

        var ex = Assert.Throws<ProtocolException>(() => ProtocolParser.ParseBody(input));
        Assert.Equal(ProtocolErrorCodes.InvalidJson, ex.Code);
    }

    [Fact]
    public void Parse_rejects_trailing_commas()
    {
        var input = """
            BEGIN_REQUEST
            {
              "version": 1,
              "id": "x",
              "commands": [
                { "type": "tree", },
              ],
            }
            END_REQUEST
            """;

        var ex = Assert.Throws<ProtocolException>(() => ProtocolParser.ParseBody(input));
        Assert.Equal(ProtocolErrorCodes.InvalidJson, ex.Code);
    }

    [Fact]
    public void Parse_rejects_javascript_style_comments()
    {
        var input = """
            {
              "version": 1, // a comment
              "id": "x",
              "commands": [{ "type": "tree" }]
            }
            """;

        var ex = Assert.Throws<ProtocolException>(() => ProtocolParser.ParseBody(input));
        Assert.Equal(ProtocolErrorCodes.InvalidJson, ex.Code);
    }

    [Fact]
    public void ParseAndValidate_throws_on_invalid_request()
    {
        var input = """
            { "version": "1.0", "id": "x", "commands": [] }
            """;

        var ex = Assert.Throws<ProtocolException>(() => ProtocolParser.ParseBodyAndValidate(input));
        Assert.Equal(ProtocolErrorCodes.EmptyCommandSet, ex.Code);
    }
}
