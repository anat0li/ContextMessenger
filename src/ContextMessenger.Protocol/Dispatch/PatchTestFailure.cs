namespace ContextMessenger.Protocol.Dispatch;

/// <summary>
/// A single failed test case from a patch's test stage, surfaced on the patch outcome so the host
/// can show it on the review page without re-parsing the serialized response.
/// </summary>
public sealed record PatchTestFailure
{
    /// <summary>Test identifier reported by the runner (e.g. fully-qualified test name), when present.</summary>
    public string? Code { get; init; }

    /// <summary>Source path reported by the runner (as emitted; may be absent for many runners).</summary>
    public string? Path { get; init; }

    public int? Line { get; init; }

    public int? Column { get; init; }

    public required string Message { get; init; }
}
