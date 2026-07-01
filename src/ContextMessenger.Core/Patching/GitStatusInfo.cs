namespace ContextMessenger.Core.Patching;

public sealed record GitStatusInfo
{
    public bool IsRepository { get; init; }

    public bool IsClean { get; init; }

    public string? Branch { get; init; }

    public string? HeadSha { get; init; }

    public IReadOnlyList<GitStatusFile> ChangedFiles { get; init; } = [];
}

public sealed record GitStatusFile
{
    public required string Path { get; init; }

    public required string Status { get; init; }
}
