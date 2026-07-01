using ContextMessenger.Protocol.Review;

namespace ContextMessenger.App.Wpf.Patching;

/// <summary>
/// Routes review actions to the loop that currently owns the held patch. The shared review
/// view-model depends on this stable instance; the host points <see cref="Target"/> at the
/// owning loop's live actions when a patch is held, and clears it when the patch is resolved.
/// </summary>
public sealed class HeldPatchActionsRouter : IHeldPatchActions
{
    public IHeldPatchActions? Target { get; set; }

    public Task SendAsync(IReadOnlyList<ReviewerComment> comments) =>
        Target?.SendAsync(comments) ?? Task.CompletedTask;

    public Task AcceptAsync() => Target?.AcceptAsync() ?? Task.CompletedTask;

    public Task RevertAsync() => Target?.RevertAsync() ?? Task.CompletedTask;

    public Task RefreshAsync() => Target?.RefreshAsync() ?? Task.CompletedTask;

    public PatchReviewSnapshot GetSnapshot() => Target?.GetSnapshot() ?? PatchReviewSnapshot.Empty;

    public string? GetFileDiff(string path) => Target?.GetFileDiff(path);

    public string? GetFileContent(string path) => Target?.GetFileContent(path);
}
