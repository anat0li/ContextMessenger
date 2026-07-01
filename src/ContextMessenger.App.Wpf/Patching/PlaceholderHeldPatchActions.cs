using ContextMessenger.Protocol.Review;

namespace ContextMessenger.App.Wpf.Patching;

/// <summary>
/// TEMPORARY no-op actions so the review view-model can be constructed and the toolbar
/// rendered before the live loop integration lands. With no active held interaction every
/// command's CanExecute is false, so none of these run yet. Replaced by the loop-backed
/// implementation in the actions-wiring slice.
/// </summary>
public sealed class PlaceholderHeldPatchActions : IHeldPatchActions
{
    public Task SendAsync(IReadOnlyList<ReviewerComment> comments) => Task.CompletedTask;

    public Task AcceptAsync() => Task.CompletedTask;

    public Task RevertAsync() => Task.CompletedTask;

    public Task RefreshAsync() => Task.CompletedTask;

    public PatchReviewSnapshot GetSnapshot() => PatchReviewSnapshot.Empty;

    public string? GetFileDiff(string path) => null;

    public string? GetFileContent(string path) => null;
}
