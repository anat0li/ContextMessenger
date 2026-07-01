using System.Text.Json;
using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.Protocol.Tests;

public sealed class PatchBuildErrorExtractorTests
{
    [Fact]
    public void Returns_only_error_diagnostics_with_their_fields()
    {
        var build = JsonDocument.Parse(
            """
            {
              "status": "failed",
              "diagnostics": [
                { "kind": "warning", "code": "CS0168", "path": "a.cs", "line": 1, "message": "unused" },
                { "kind": "error", "code": "CS1002", "path": "src/A.cs", "line": 12, "column": 5, "message": "; expected" }
              ]
            }
            """).RootElement;

        var errors = PatchBuildErrorExtractor.FromBuildElement(build);

        var error = Assert.Single(errors);
        Assert.Equal("CS1002", error.Code);
        Assert.Equal("src/A.cs", error.Path);
        Assert.Equal(12, error.Line);
        Assert.Equal(5, error.Column);
        Assert.Equal("; expected", error.Message);
    }

    [Fact]
    public void Formats_stage_diagnostic_without_path_as_visible_stage_error()
    {
        var build = JsonDocument.Parse(
            """
            {
              "status": "failed",
              "diagnostics": [
                { "kind": "error", "code": "invalid_patch_policy", "message": "Unsupported build policy 'bogus'." }
              ]
            }
            """).RootElement;

        var error = Assert.Single(PatchBuildErrorExtractor.FromBuildElement(build, "build"));

        Assert.Equal("invalid_patch_policy", error.Code);
        Assert.Null(error.Path);
        Assert.Equal("build stage failed: Unsupported build policy 'bogus'.", error.Message);
    }

    [Fact]
    public void Returns_only_warning_diagnostics_with_their_fields()
    {
        var build = JsonDocument.Parse(
            """
            {
              "status": "passed",
              "diagnostics": [
                { "kind": "warning", "code": "CS0168", "path": "a.cs", "line": 1, "column": 2, "message": "unused" },
                { "kind": "error", "code": "CS1002", "path": "src/A.cs", "line": 12, "message": "; expected" }
              ]
            }
            """).RootElement;

        var warnings = PatchBuildErrorExtractor.WarningsFromBuildElement(build);

        var warning = Assert.Single(warnings);
        Assert.Equal("CS0168", warning.Code);
        Assert.Equal("a.cs", warning.Path);
        Assert.Equal(1, warning.Line);
        Assert.Equal(2, warning.Column);
        Assert.Equal("unused", warning.Message);
    }

    [Theory]
    [InlineData("\"skipped\"")]
    [InlineData("{ \"status\": \"passed\" }")]
    [InlineData("{ \"diagnostics\": [] }")]
    [InlineData("{ \"diagnostics\": [ { \"kind\": \"warning\", \"message\": \"w\" } ] }")]
    public void Returns_empty_when_no_error_diagnostics(string json)
    {
        Assert.Empty(PatchBuildErrorExtractor.FromBuildElement(JsonDocument.Parse(json).RootElement));
    }

    [Fact]
    public void Stage_summary_extracts_status_policy_counts_and_duration()
    {
        var stage = JsonDocument.Parse(
            """
            {
              "status": "failed",
              "policy": "solution",
              "durationMs": 123,
              "exitCode": 1,
              "totalTests": 4,
              "executedTests": 3,
              "passedTests": 2,
              "failedTests": 1,
              "skippedTests": 1
            }
            """).RootElement;

        var summary = PatchStageSummary.FromStageElement(stage);

        Assert.Equal("failed", summary.Status);
        Assert.Equal("solution", summary.Policy);
        Assert.Equal(123, summary.DurationMs);
        Assert.Equal(1, summary.ExitCode);
        Assert.Equal(4, summary.TotalTests);
        Assert.Equal(3, summary.ExecutedTests);
        Assert.Equal(2, summary.PassedTests);
        Assert.Equal(1, summary.FailedTests);
        Assert.Equal(1, summary.SkippedTests);
    }
}
