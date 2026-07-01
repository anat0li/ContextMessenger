using System.Text.Json;
using ContextMessenger.Protocol.Dispatch;
using ContextMessenger.Protocol.Wire;

namespace ContextMessenger.Protocol.Tests;

public sealed class PatchCommentReplyExtractorTests
{
    private static ContextCommand Command(string parametersJson) => new()
    {
        Type = "amend_patch",
        Parameters = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(parametersJson)!,
    };

    [Fact]
    public void Reads_comment_replies_by_id()
    {
        var command = Command(
            """{ "commentReplies": [ { "id": "c-1", "reply": "fixed", "path": "src/A.cs", "line": 12, "openIssue": true }, { "id": "c-2", "reply": "won't fix", "openIssue": false }, { "id": "c-3", "reply": "no flag" } ] }""");

        var replies = PatchCommentReplyExtractor.FromCommand(command);

        Assert.Equal(3, replies.Count);
        Assert.Equal("c-1", replies[0].Id);
        Assert.Equal("fixed", replies[0].Reply);
        Assert.Equal("src/A.cs", replies[0].Path);
        Assert.Equal(12, replies[0].Line);
        Assert.True(replies[0].OpenIssue);
        Assert.Equal("c-2", replies[1].Id);
        Assert.Equal("", replies[1].Path);
        Assert.Equal(0, replies[1].Line);
        Assert.False(replies[1].OpenIssue);
        Assert.Null(replies[2].OpenIssue);
    }

    [Theory]
    [InlineData("{ }")]
    [InlineData("""{ "commentReplies": [] }""")]
    [InlineData("""{ "commentReplies": [ { "reply": "no id" } ] }""")]
    public void Empty_when_absent_or_malformed(string parametersJson)
    {
        Assert.Empty(PatchCommentReplyExtractor.FromCommand(Command(parametersJson)));
    }
}
