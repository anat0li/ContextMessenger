using System.Text.Json;
using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.Protocol.Tests;

public sealed class PatchTestFailureExtractorTests
{
    [Fact]
    public void Returns_only_test_diagnostics_with_their_fields()
    {
        var tests = JsonDocument.Parse(
            """
            {
              "status": "failed",
              "diagnostics": [
                { "kind": "error", "message": "a build error" },
                { "kind": "test", "code": "Ns.MyTests.Fails", "path": "src/MyTests.cs", "line": 20, "message": "Assert.True() Failure" }
              ]
            }
            """).RootElement;

        var failures = PatchTestFailureExtractor.FromTestsElement(tests);

        var failure = Assert.Single(failures);
        Assert.Equal("Ns.MyTests.Fails", failure.Code);
        Assert.Equal("src/MyTests.cs", failure.Path);
        Assert.Equal(20, failure.Line);
        Assert.Contains("Failure", failure.Message);
    }

    [Theory]
    [InlineData("\"skipped\"")]
    [InlineData("{ \"status\": \"ok\" }")]
    [InlineData("{ \"diagnostics\": [] }")]
    [InlineData("{ \"diagnostics\": [ { \"kind\": \"error\", \"message\": \"e\" } ] }")]
    public void Returns_empty_when_no_test_diagnostics(string json)
    {
        Assert.Empty(PatchTestFailureExtractor.FromTestsElement(JsonDocument.Parse(json).RootElement));
    }
}
