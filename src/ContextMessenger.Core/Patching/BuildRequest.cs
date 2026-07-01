namespace ContextMessenger.Core.Patching;

public sealed record BuildRequest
{
    public string? Path { get; init; }

    public string Configuration { get; init; } = "Debug";

    public int TimeoutSeconds { get; init; } = 120;

    public bool TreatWarningsAsErrors { get; init; }
}
