namespace ContextMessenger.Core.Patching;

public sealed record BuildResult
{
    public required string Status { get; init; }

    public string? Path { get; init; }

    public string? Configuration { get; init; }

    public int DurationMs { get; init; }

    public int? ExitCode { get; init; }

    public string Stdout { get; init; } = "";

    public bool StdoutTruncated { get; init; }

    public string Stderr { get; init; } = "";

    public bool StderrTruncated { get; init; }

    public IReadOnlyList<BuildDiagnostic> Diagnostics { get; init; } = [];
}
