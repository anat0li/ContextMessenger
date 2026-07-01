namespace ContextMessenger.Core.Patching;

public interface IFilePatchApplier
{
    PatchApplyResult Apply(IReadOnlyList<PatchFileOperation> operations);
}
