namespace ContextMessenger.Core.Patching;

public sealed record PatchApplyResult
{
    public required IReadOnlyList<string> ChangedFiles { get; init; }
}
