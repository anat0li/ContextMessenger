namespace ContextMessenger.Core.Patching;

public sealed record TestRequest
{
    public string Policy { get; init; } = "none";

    public string? Path { get; init; }

    public IReadOnlyList<string> Projects { get; init; } = [];

    public string? Filter { get; init; }

    public string Configuration { get; init; } = "Debug";

    public int TimeoutSeconds { get; init; } = 120;
}
