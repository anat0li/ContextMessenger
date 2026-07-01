using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ContextMessenger.Core.Patching;

namespace ContextMessenger.Patching;

public sealed class DotnetTestRunner : ITestRunner
{
    private const int MaxCapturedOutputChars = 1200;

    private static readonly Regex DiagnosticRegex = new(
        @"^(?<path>.*)\((?<line>\d+),(?<column>\d+)\):\s(?<kind>error|warning)\s(?<code>[A-Z]+\d+):\s(?<message>.*?)(?:\s\[(?<project>.*)\])?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Matches a .NET stack-trace frame's source location: "... in <path>:line <n>".
    private static readonly Regex StackFrameLocationRegex = new(
        @"\sin\s(?<path>.+?):line\s(?<line>\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _rootPath;

    public DotnetTestRunner(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Root path is required.", nameof(rootPath));

        _rootPath = Path.GetFullPath(rootPath);
    }

    public TestResult Run(TestRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var policy = string.IsNullOrWhiteSpace(request.Policy) ? "none" : request.Policy.ToLowerInvariant();
        var targets = ResolveTargets(policy, request);
        var timeoutSeconds = request.TimeoutSeconds > 0 ? request.TimeoutSeconds : 120;
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var diagnostics = new List<BuildDiagnostic>();
        var exitCode = 0;
        var counters = TestCounters.Empty;
        var stopwatch = Stopwatch.StartNew();

        foreach (var target in targets)
        {
            var resultPath = CreateResultsPath(target, request.Configuration);
            var artifactsPath = CreateArtifactsPath(target, request.Configuration);
            using var process = CreateProcess(target, request, resultPath, artifactsPath);

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                    stdout.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                    stderr.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var remaining = Math.Max(1, timeoutSeconds * 1000 - (int)stopwatch.ElapsedMilliseconds);
            var exited = process.WaitForExit(remaining);
            if (!exited)
            {
                TryKill(process);
                stopwatch.Stop();
                var timedOutStdout = LimitOutput(stdout.ToString());
                var timedOutStderr = LimitOutput(stderr.ToString());
                return new TestResult
                {
                    Status = "timeout",
                    Path = ResolveDisplayPath(policy, request),
                    Projects = NormalizePaths(request.Projects),
                    Filter = request.Filter,
                    Configuration = request.Configuration,
                    DurationMs = (int)stopwatch.ElapsedMilliseconds,
                    TotalTests = counters.Total,
                    ExecutedTests = counters.Executed,
                    PassedTests = counters.Passed,
                    FailedTests = counters.Failed,
                    SkippedTests = counters.Skipped,
                    Stdout = timedOutStdout.Text,
                    StdoutTruncated = timedOutStdout.Truncated,
                    Stderr = timedOutStderr.Text,
                    StderrTruncated = timedOutStderr.Truncated,
                    Diagnostics = DeduplicateDiagnostics(diagnostics),
                };
            }

            process.WaitForExit();
            exitCode = exitCode == 0 ? process.ExitCode : exitCode;
            var trx = ParseTrxResultDirectory(Path.Combine(_rootPath, resultPath));
            // Test diagnostics carry the source location extracted from the stack trace as an
            // absolute path; make it root-relative so it matches changed-file paths (and the model).
            diagnostics.AddRange(trx.Diagnostics.Select(d =>
                string.IsNullOrEmpty(d.Path) ? d : d with { Path = NormalizePath(d.Path) }));
            counters += trx.Counters;
        }

        stopwatch.Stop();
        var outText = stdout.ToString();
        var errText = stderr.ToString();
        diagnostics.AddRange(ParseBuildDiagnostics(outText + errText));
        var limitedStdout = LimitOutput(outText);
        var limitedStderr = LimitOutput(errText);
        return CreateCompletedResult(
            request,
            policy,
            ResolveDisplayPath(policy, request),
            NormalizePaths(request.Projects),
            (int)stopwatch.ElapsedMilliseconds,
            exitCode,
            counters,
            diagnostics,
            limitedStdout,
            limitedStderr);
    }

    private Process CreateProcess(string target, TestRequest request, string resultPath, string artifactsPath)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = _rootPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };

        process.StartInfo.ArgumentList.Add("test");
        process.StartInfo.ArgumentList.Add(target);
        process.StartInfo.ArgumentList.Add("--configuration");
        process.StartInfo.ArgumentList.Add(string.IsNullOrWhiteSpace(request.Configuration) ? "Debug" : request.Configuration);
        process.StartInfo.ArgumentList.Add("--verbosity");
        process.StartInfo.ArgumentList.Add("minimal");
        process.StartInfo.ArgumentList.Add("--logger");
        process.StartInfo.ArgumentList.Add("trx");
        process.StartInfo.ArgumentList.Add("--results-directory");
        process.StartInfo.ArgumentList.Add(resultPath);
        process.StartInfo.ArgumentList.Add("--artifacts-path");
        process.StartInfo.ArgumentList.Add(artifactsPath);
        process.StartInfo.ArgumentList.Add("--disable-build-servers");
        process.StartInfo.ArgumentList.Add("-p:UseSharedCompilation=false");
        process.StartInfo.ArgumentList.Add("/nr:false");

        if (!string.IsNullOrWhiteSpace(request.Filter))
        {
            process.StartInfo.ArgumentList.Add("--filter");
            process.StartInfo.ArgumentList.Add(request.Filter);
        }

        return process;
    }

    private IReadOnlyList<string> ResolveTargets(string policy, TestRequest request)
    {
        return policy switch
        {
            "all" => [ResolveSolutionPath(request.Path)],
            "projects" or "filter" => request.Projects.Select(ResolveRequestedPath).ToArray(),
            _ => throw new PatchValidationException("unsupported_patch_policy", $"tests.policy '{request.Policy}' is not supported; use none, all, projects, or filter."),
        };
    }

    private string ResolveSolutionPath(string? requestedPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedPath))
            return ResolveRequestedPath(requestedPath);

        var solution = Directory.EnumerateFiles(_rootPath, "*.slnx", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(_rootPath, "*.sln", SearchOption.TopDirectoryOnly))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (solution is null)
            throw new PatchValidationException("file_not_found", "No .slnx or .sln file found in the active root.");

        return solution;
    }

    private string ResolveRequestedPath(string requestedPath)
    {
        var candidate = Path.GetFullPath(Path.Combine(_rootPath, requestedPath));
        if (!IsUnderRoot(candidate, _rootPath))
            throw new PatchValidationException("path_outside_sandbox", $"Test path is outside the root: {requestedPath}");
        if (!File.Exists(candidate))
            throw new PatchValidationException("file_not_found", $"Test path not found: {requestedPath}");

        return candidate;
    }

    private string CreateArtifactsPath(string target, string configuration)
    {
        var basePath = Path.Combine(_rootPath, PatchWorkspace.ControlDirectoryName, "patch-test", "artifacts");
        Directory.CreateDirectory(basePath);

        var relativePath = Path.Combine(
            PatchWorkspace.ControlDirectoryName,
            "patch-test",
            "artifacts",
            StableKey(target, configuration));
        Directory.CreateDirectory(Path.Combine(_rootPath, relativePath));
        return relativePath;
    }

    private string CreateResultsPath(string target, string configuration)
    {
        var basePath = Path.Combine(_rootPath, PatchWorkspace.ControlDirectoryName, "patch-test", "results");
        Directory.CreateDirectory(basePath);
        PruneLegacyResultDirectories(Path.Combine(_rootPath, PatchWorkspace.ControlDirectoryName, "patch-test"));

        var relativePath = Path.Combine(
            PatchWorkspace.ControlDirectoryName,
            "patch-test",
            "results",
            StableKey(target, configuration));
        var fullPath = Path.Combine(_rootPath, relativePath);
        if (Directory.Exists(fullPath) && !TryDeleteDirectory(fullPath))
        {
            relativePath = Path.Combine(
                PatchWorkspace.ControlDirectoryName,
                "patch-test",
                "results",
                StableKey(target, configuration) + "-" + Guid.NewGuid().ToString("N"));
            fullPath = Path.Combine(_rootPath, relativePath);
        }

        Directory.CreateDirectory(fullPath);
        return relativePath;
    }

    private string StableKey(string targetPath, string configuration)
    {
        var name = SanitizeFileName(Path.GetFileNameWithoutExtension(targetPath));
        var input = $"{NormalizePath(targetPath)}|{configuration}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))[..12].ToLowerInvariant();
        return $"{name}-{configuration.ToLowerInvariant()}-{hash}";
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) ? '-' : c).ToArray();
        return string.IsNullOrWhiteSpace(value) ? "target" : new string(chars);
    }

    private static void PruneLegacyResultDirectories(string patchTestPath)
    {
        if (!Directory.Exists(patchTestPath))
            return;

        foreach (var directory in Directory.EnumerateDirectories(patchTestPath))
        {
            var name = Path.GetFileName(directory);
            if (string.Equals(name, "artifacts", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "results", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TryDeleteDirectory(directory);
        }
    }

    internal static DotnetTestRunSummary ParseTrxResultDirectory(string resultPath)
    {
        var fullPath = Path.GetFullPath(resultPath);
        if (!Directory.Exists(fullPath))
            return DotnetTestRunSummary.Empty;

        var diagnostics = new List<BuildDiagnostic>();
        var counters = TestCounters.Empty;
        foreach (var trx in Directory.EnumerateFiles(fullPath, "*.trx", SearchOption.AllDirectories))
        {
            XDocument document;
            try
            {
                document = XDocument.Load(trx);
            }
            catch
            {
                continue;
            }

            var countersElement = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "Counters");
            if (countersElement is not null)
                counters += ParseCounters(countersElement);

            foreach (var unitTestResult in document.Descendants().Where(e => e.Name.LocalName == "UnitTestResult"))
            {
                var outcome = unitTestResult.Attribute("outcome")?.Value;
                if (!string.Equals(outcome, "Failed", StringComparison.OrdinalIgnoreCase))
                    continue;

                var testName = unitTestResult.Attribute("testName")?.Value
                    ?? unitTestResult.Attribute("testId")?.Value
                    ?? "test";
                var errorInfo = unitTestResult.Descendants().FirstOrDefault(e => e.Name.LocalName == "ErrorInfo");
                var message = errorInfo?.Descendants().FirstOrDefault(e => e.Name.LocalName == "Message")?.Value
                    ?? "Test failed.";
                var stackTrace = errorInfo?.Descendants().FirstOrDefault(e => e.Name.LocalName == "StackTrace")?.Value;
                var (sourcePath, sourceLine) = ExtractSourceLocation(stackTrace);
                diagnostics.Add(new BuildDiagnostic
                {
                    Kind = "test",
                    Code = testName,
                    Path = sourcePath,
                    Line = sourceLine,
                    Message = string.IsNullOrWhiteSpace(stackTrace)
                        ? message.Trim()
                        : $"{message.Trim()}\n{stackTrace.Trim()}",
                });
            }
        }

        return new DotnetTestRunSummary(diagnostics, counters);
    }

    // Pulls the first source path + line out of a .NET stack trace, so a failed test can be
    // linked to its source file (jump only succeeds when that file is part of the patch).
    private static (string? Path, int? Line) ExtractSourceLocation(string? stackTrace)
    {
        if (string.IsNullOrEmpty(stackTrace))
            return (null, null);

        var match = StackFrameLocationRegex.Match(stackTrace);
        if (!match.Success)
            return (null, null);

        var line = int.TryParse(match.Groups["line"].Value, out var n) ? n : (int?)null;
        return (match.Groups["path"].Value, line);
    }

    internal static TestResult CreateCompletedResultForTesting(
        TestRequest request,
        string policy,
        int exitCode,
        DotnetTestRunSummary summary) =>
        CreateCompletedResult(
            request,
            policy,
            path: request.Path,
            projects: request.Projects,
            durationMs: 10,
            exitCode,
            summary.Counters,
            summary.Diagnostics,
            (Text: "", Truncated: false),
            (Text: "", Truncated: false));

    private static TestResult CreateCompletedResult(
        TestRequest request,
        string policy,
        string? path,
        IReadOnlyList<string> projects,
        int durationMs,
        int exitCode,
        TestCounters counters,
        IReadOnlyList<BuildDiagnostic> diagnostics,
        (string Text, bool Truncated) stdout,
        (string Text, bool Truncated) stderr)
    {
        var allDiagnostics = diagnostics.ToList();
        var noTestsMatchedFilter = string.Equals(policy, "filter", StringComparison.Ordinal) &&
                                   exitCode == 0 &&
                                   counters.Executed == 0;
        if (noTestsMatchedFilter)
        {
            allDiagnostics.Add(new BuildDiagnostic
            {
                Kind = "error",
                Code = "no_tests_matched_filter",
                Message = $"No tests matched filter '{request.Filter}'.",
            });
        }

        return new TestResult
        {
            Status = exitCode == 0 && !noTestsMatchedFilter ? "ok" : "failed",
            Path = path,
            Projects = projects,
            Filter = request.Filter,
            Configuration = request.Configuration,
            DurationMs = durationMs,
            ExitCode = exitCode,
            TotalTests = counters.Total,
            ExecutedTests = counters.Executed,
            PassedTests = counters.Passed,
            FailedTests = counters.Failed,
            SkippedTests = counters.Skipped,
            Stdout = stdout.Text,
            StdoutTruncated = stdout.Truncated,
            Stderr = stderr.Text,
            StderrTruncated = stderr.Truncated,
            Diagnostics = DeduplicateDiagnostics(allDiagnostics),
        };
    }

    private static TestCounters ParseCounters(XElement counters) => new(
        Total: ParseCounter(counters, "total"),
        Executed: ParseCounter(counters, "executed"),
        Passed: ParseCounter(counters, "passed"),
        Failed: ParseCounter(counters, "failed"),
        Skipped: ParseCounter(counters, "notExecuted"));

    private static int ParseCounter(XElement counters, string name) =>
        int.TryParse(counters.Attribute(name)?.Value, out var value) ? value : 0;

    private IReadOnlyList<BuildDiagnostic> ParseBuildDiagnostics(string text)
    {
        var diagnostics = new List<BuildDiagnostic>();
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var match = DiagnosticRegex.Match(line);
            if (!match.Success)
                continue;

            diagnostics.Add(new BuildDiagnostic
            {
                Kind = match.Groups["kind"].Value.ToLowerInvariant(),
                Code = match.Groups["code"].Value,
                Path = NormalizePath(match.Groups["path"].Value),
                Line = int.Parse(match.Groups["line"].Value),
                Column = int.Parse(match.Groups["column"].Value),
                Message = match.Groups["message"].Value.Trim(),
            });
        }

        return diagnostics;
    }

    private static IReadOnlyList<BuildDiagnostic> DeduplicateDiagnostics(IReadOnlyList<BuildDiagnostic> diagnostics)
    {
        var result = new List<BuildDiagnostic>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var diagnostic in diagnostics)
        {
            var key = string.Join(
                '\u001f',
                diagnostic.Kind,
                diagnostic.Code ?? "",
                diagnostic.Path ?? "",
                diagnostic.Line?.ToString() ?? "",
                diagnostic.Column?.ToString() ?? "",
                diagnostic.Message);
            if (seen.Add(key))
                result.Add(diagnostic);
        }

        return result;
    }

    private static (string Text, bool Truncated) LimitOutput(string text)
    {
        if (text.Length <= MaxCapturedOutputChars)
            return (text, false);

        const string marker = "[output truncated]\r\n";
        var keep = Math.Max(0, MaxCapturedOutputChars - marker.Length);
        return (marker + text[^keep..], true);
    }

    private string? ResolveDisplayPath(string policy, TestRequest request)
    {
        if (!string.Equals(policy, "all", StringComparison.OrdinalIgnoreCase))
            return request.Path;

        return NormalizePath(ResolveSolutionPath(request.Path));
    }

    private IReadOnlyList<string> NormalizePaths(IReadOnlyList<string> paths) =>
        paths.Select(path => NormalizePath(ResolveRequestedPath(path))).ToArray();

    private string NormalizePath(string path)
    {
        var full = Path.GetFullPath(path);
        if (IsUnderRoot(full, _rootPath))
            return Path.GetRelativePath(_rootPath, full).Replace('\\', '/');

        return path.Replace('\\', '/');
    }

    private static bool IsUnderRoot(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullRoot, comparison) ||
               string.Equals(Path.TrimEndingDirectorySeparator(fullPath), Path.TrimEndingDirectorySeparator(fullRoot), comparison);
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Process may have exited between timeout detection and kill.
        }
    }

    private static bool TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            return true;
        }
        catch
        {
            // Ignored artifacts can be cleaned manually if a process still holds a lock.
            return false;
        }
    }

    internal sealed record DotnetTestRunSummary(IReadOnlyList<BuildDiagnostic> Diagnostics, TestCounters Counters)
    {
        public static DotnetTestRunSummary Empty { get; } = new([], TestCounters.Empty);
    }

    internal sealed record TestCounters(int Total, int Executed, int Passed, int Failed, int Skipped)
    {
        public static TestCounters Empty { get; } = new(0, 0, 0, 0, 0);

        public static TestCounters operator +(TestCounters left, TestCounters right) => new(
            left.Total + right.Total,
            left.Executed + right.Executed,
            left.Passed + right.Passed,
            left.Failed + right.Failed,
            left.Skipped + right.Skipped);
    }
}
