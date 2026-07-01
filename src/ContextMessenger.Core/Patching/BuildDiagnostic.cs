namespace ContextMessenger.Core.Patching;

public sealed record BuildDiagnostic
{
    public required string Kind { get; init; }

    public string? Code { get; init; }

    public string? Path { get; init; }

    public int? Line { get; init; }

    public int? Column { get; init; }

    public required string Message { get; init; }
}
