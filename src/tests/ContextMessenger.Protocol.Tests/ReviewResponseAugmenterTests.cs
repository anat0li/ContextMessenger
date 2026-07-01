using ContextMessenger.Protocol;
using ContextMessenger.Protocol.Review;

namespace ContextMessenger.Protocol.Tests;

public sealed class ReviewResponseAugmenterTests
{
    private const string Block =
        "BEGIN_RESPONSE\n{\n  \"version\": \"1.0\",\n  \"id\": \"r1\",\n  \"results\": []\n}\nEND_RESPONSE";

    private static readonly ReviewerComment[] Comments =
    [
        new() { Id = "c-1", Path = "src/A.cs", Line = 5, Comment = "please simplify", OpenIssue = true },
    ];

    [Fact]
    public void Injects_reviewer_comments_with_ids_inside_the_block()
    {
        var result = ReviewResponseAugmenter.Augment(Block, Comments);

        Assert.StartsWith(ProtocolDelimiters.BeginResponse, result);
        Assert.EndsWith(ProtocolDelimiters.EndResponse, result);
        Assert.Contains("reviewerComments", result);
        Assert.Contains("c-1", result);
        Assert.Contains("src/A.cs", result);
        Assert.Contains("please simplify", result);
        Assert.Contains("openIssue", result);
        Assert.Contains("true", result);
    }

    [Fact]
    public void No_comments_returns_unchanged()
    {
        Assert.Equal(Block, ReviewResponseAugmenter.Augment(Block, []));
    }

    [Fact]
    public void Unrecognizable_text_returns_unchanged()
    {
        Assert.Equal("not a response block", ReviewResponseAugmenter.Augment("not a response block", Comments));
    }
}
