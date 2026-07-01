namespace ContextMessenger.Core.Patching;

public sealed record PatchWarning
{
    public required string Code { get; init; }

    public required string Message { get; init; }

    public string? Path { get; init; }

    public int? EditIndex { get; init; }

    public string? Kind { get; init; }
}
