namespace ContextMessenger.Core.Patching;

public sealed record PatchFileState
{
    public required string Path { get; init; }

    public required string Operation { get; init; }

    public string? OldContentHash { get; init; }

    public string? CurrentContentHash { get; init; }

    public int LastRevision { get; init; }
}
