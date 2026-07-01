namespace ContextMessenger.Protocol.Dispatch;

/// <summary>
/// A single build warning from a patch's build stage, surfaced on the patch outcome so the host can
/// show it on the review page without re-parsing the serialized response.
/// </summary>
public sealed record PatchBuildWarning
{
    public string? Code { get; init; }

    /// <summary>Source path reported by the compiler (as emitted; may be absolute or project-relative).</summary>
    public string? Path { get; init; }

    public int? Line { get; init; }

    public int? Column { get; init; }

    public required string Message { get; init; }
}
