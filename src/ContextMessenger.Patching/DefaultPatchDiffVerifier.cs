using ContextMessenger.Core.Patching;

namespace ContextMessenger.Patching;

public sealed class DefaultPatchDiffVerifier : IPatchDiffVerifier
{
    public void Verify(IReadOnlyList<PatchFileOperation> operations, IReadOnlyList<GitStatusFile> changedFiles)
    {
        var expected = operations.Select(op => NormalizePath(op.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actual = changedFiles.Select(f => NormalizePath(f.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!expected.SetEquals(actual))
        {
            var expectedText = string.Join(", ", expected.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            var actualText = string.Join(", ", actual.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            throw new PatchValidationException(
                "diff_verification_failed",
                $"Applied diff paths did not match patch spec. Expected [{expectedText}], actual [{actualText}].");
        }
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}
