using System.Text.RegularExpressions;
using ContextMessenger.FileSystem;
using ContextMessenger.Protocol.Commands;
using ContextMessenger.Protocol.Dispatch;
using ContextMessenger.Protocol.Wire;

namespace ContextMessenger.Protocol.Tests;

public sealed class ServerTimeUtcTests
{
    private static readonly Regex Iso8601SecondsUtc =
        new(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$", RegexOptions.Compiled);

    [Fact]
    public void Dispatch_response_has_iso8601_serverTimeUtc()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForFileSystem(new FileSystemContextService(temp.Path));

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands = [new ContextCommand { Type = CommandTypes.Tree }],
        });

        Assert.Matches(Iso8601SecondsUtc, response.ServerTimeUtc);
    }

    [Fact]
    public void WriteError_includes_serverTimeUtc()
    {
        var output = ProtocolWriter.WriteError("abc", ProtocolErrorCodes.InvalidJson, "bad");

        Assert.Contains("\"serverTimeUtc\"", output);
        var match = Regex.Match(output, "\"serverTimeUtc\": \"(?<time>[^\"]+)\"");
        Assert.True(match.Success);
        Assert.Matches(Iso8601SecondsUtc, match.Groups["time"].Value);
    }

    [Fact]
    public void ProcessRequests_output_includes_serverTimeUtc()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForFileSystem(new FileSystemContextService(temp.Path));

        var input = """
            { "version": "1.0", "id": "11111111-1111-1111-1111-111111111111", "commands": [{ "type": "tree" }] }
            """;
        var output = dispatcher.ProcessRequests([input]);

        Assert.Contains("\"serverTimeUtc\"", output);
    }
}
