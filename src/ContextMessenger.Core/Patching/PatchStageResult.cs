namespace ContextMessenger.Core.Patching;

public sealed record PatchStageResult
{
    public required string Status { get; init; }

    public string? Policy { get; init; }

    public string? Path { get; init; }

    public IReadOnlyList<string> Projects { get; init; } = [];

    public string? Filter { get; init; }

    public string? Configuration { get; init; }

    public int? DurationMs { get; init; }

    public int? ExitCode { get; init; }

    public int? TotalTests { get; init; }

    public int? ExecutedTests { get; init; }

    public int? PassedTests { get; init; }

    public int? FailedTests { get; init; }

    public int? SkippedTests { get; init; }

    public string? Stdout { get; init; }

    public bool StdoutTruncated { get; init; }

    public string? Stderr { get; init; }

    public bool StderrTruncated { get; init; }

    public IReadOnlyList<BuildDiagnostic> Diagnostics { get; init; } = [];
}
