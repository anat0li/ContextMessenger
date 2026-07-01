namespace ContextMessenger.Core.Patching;

public interface IPatchDiffVerifier
{
    void Verify(IReadOnlyList<PatchFileOperation> operations, IReadOnlyList<GitStatusFile> changedFiles);
}
