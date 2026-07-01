using ContextMessenger.Core.Patching;

namespace ContextMessenger.Patching.Tests;

public sealed class DotnetTestRunnerTests
{
    [Fact]
    public void ParseTrxResultDirectory_reads_passing_counters()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("results/pass.trx", Trx(
            total: 3,
            executed: 3,
            passed: 3,
            failed: 0,
            notExecuted: 0,
            results: ""));

        var summary = DotnetTestRunner.ParseTrxResultDirectory(Path.Combine(temp.Path, "results"));

        Assert.Equal(3, summary.Counters.Total);
        Assert.Equal(3, summary.Counters.Executed);
        Assert.Equal(3, summary.Counters.Passed);
        Assert.Equal(0, summary.Counters.Failed);
        Assert.Equal(0, summary.Counters.Skipped);
        Assert.Empty(summary.Diagnostics);
    }

    [Fact]
    public void ParseTrxResultDirectory_extracts_failed_test_diagnostics()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("results/fail.trx", Trx(
            total: 2,
            executed: 2,
            passed: 1,
            failed: 1,
            notExecuted: 0,
            results:
            """
            <UnitTestResult testName="PatchTests.Fails" outcome="Failed">
              <Output>
                <ErrorInfo>
                  <Message>Expected true but was false.</Message>
                  <StackTrace>at PatchTests.Fails()</StackTrace>
                </ErrorInfo>
              </Output>
            </UnitTestResult>
            """));

        var summary = DotnetTestRunner.ParseTrxResultDirectory(Path.Combine(temp.Path, "results"));

        Assert.Equal(2, summary.Counters.Total);
        Assert.Equal(1, summary.Counters.Failed);
        var diagnostic = Assert.Single(summary.Diagnostics);
        Assert.Equal("test", diagnostic.Kind);
        Assert.Equal("PatchTests.Fails", diagnostic.Code);
        Assert.Contains("Expected true but was false.", diagnostic.Message);
        Assert.Contains("PatchTests.Fails()", diagnostic.Message);
    }

    [Fact]
    public void ParseTrxResultDirectory_extracts_source_location_from_stack_trace()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("results/fail.trx", Trx(
            total: 1,
            executed: 1,
            passed: 0,
            failed: 1,
            notExecuted: 0,
            results:
            """
            <UnitTestResult testName="Ns.MyTests.Fails" outcome="Failed">
              <Output>
                <ErrorInfo>
                  <Message>Assert.True() Failure</Message>
                  <StackTrace>   at Ns.MyTests.Fails() in C:\repo\src\tests\MyTests.cs:line 42</StackTrace>
                </ErrorInfo>
              </Output>
            </UnitTestResult>
            """));

        var summary = DotnetTestRunner.ParseTrxResultDirectory(Path.Combine(temp.Path, "results"));

        var diagnostic = Assert.Single(summary.Diagnostics);
        Assert.Equal(@"C:\repo\src\tests\MyTests.cs", diagnostic.Path);
        Assert.Equal(42, diagnostic.Line);
    }

    [Fact]
    public void ParseTrxResultDirectory_aggregates_multiple_trx_files()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("results/one.trx", Trx(2, 2, 2, 0, 0, ""));
        temp.CreateFile("results/two.trx", Trx(4, 3, 2, 1, 1, """
            <UnitTestResult testName="PatchTests.SecondFails" outcome="Failed">
              <Output>
                <ErrorInfo>
                  <Message>Second failure.</Message>
                </ErrorInfo>
              </Output>
            </UnitTestResult>
            """));

        var summary = DotnetTestRunner.ParseTrxResultDirectory(Path.Combine(temp.Path, "results"));

        Assert.Equal(6, summary.Counters.Total);
        Assert.Equal(5, summary.Counters.Executed);
        Assert.Equal(4, summary.Counters.Passed);
        Assert.Equal(1, summary.Counters.Failed);
        Assert.Equal(1, summary.Counters.Skipped);
        Assert.Equal("PatchTests.SecondFails", Assert.Single(summary.Diagnostics).Code);
    }

    [Fact]
    public void CreateCompletedResultForTesting_fails_filter_when_zero_tests_executed()
    {
        var request = new TestRequest
        {
            Policy = "filter",
            Projects = ["tests/Patch.Tests.csproj"],
            Filter = "FullyQualifiedName~DefinitelyNoSuchTest",
        };
        var summary = new DotnetTestRunner.DotnetTestRunSummary(
            [],
            new DotnetTestRunner.TestCounters(Total: 0, Executed: 0, Passed: 0, Failed: 0, Skipped: 0));

        var result = DotnetTestRunner.CreateCompletedResultForTesting(request, "filter", exitCode: 0, summary);

        Assert.Equal("failed", result.Status);
        Assert.Equal(0, result.ExecutedTests);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("error", diagnostic.Kind);
        Assert.Equal("no_tests_matched_filter", diagnostic.Code);
        Assert.Contains("DefinitelyNoSuchTest", diagnostic.Message);
    }

    private static string Trx(
        int total,
        int executed,
        int passed,
        int failed,
        int notExecuted,
        string results) =>
        $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results>
        {{results}}
          </Results>
          <ResultSummary outcome="Completed">
            <Counters total="{{total}}" executed="{{executed}}" passed="{{passed}}" failed="{{failed}}" notExecuted="{{notExecuted}}" />
          </ResultSummary>
        </TestRun>
        """;
}
