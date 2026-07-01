namespace ContextMessenger.Core.Patching;

public sealed record PatchFileOperation
{
    public required string Path { get; init; }

    public required PatchFileOperationKind Operation { get; init; }

    public string? OldContentHash { get; init; }

    public string? NewContent { get; init; }
}

public enum PatchFileOperationKind
{
    Create,
    Replace,
    Delete,
}
