using ContextMessenger.Protocol.Review;

namespace ContextMessenger.App.Wpf.Patching;

/// <summary>
/// The patch review operations on the active held patch. The live implementation (a later
/// integration slice) calls the patch service and submits responses to the chat target.
/// The review view-model depends only on this seam, so it is testable without the loop.
/// </summary>
public interface IHeldPatchActions
{
    /// <summary>
    /// Deliver the held response to the model, augmented with any reviewer <paramref name="comments"/>
    /// injected as <c>reviewerComments</c>. The patch stays active for further review.
    /// </summary>
    Task SendAsync(IReadOnlyList<ReviewerComment> comments);

    /// <summary>
    /// Accept a validated patch: deliver the accepted response, stage the files, close the
    /// transaction, and dispose the patch (closing the review page).
    /// </summary>
    Task AcceptAsync();

    /// <summary>Revert the current patch, doing whatever its present state requires.</summary>
    Task RevertAsync();

    /// <summary>Re-read patch/interaction state (e.g., after an external repository change).</summary>
    Task RefreshAsync();

    /// <summary>
    /// Project the active patch (changed files + descriptive fields) for the review page, read
    /// from the live patch service. Returns <see cref="PatchReviewSnapshot.Empty"/> when no
    /// patch is active.
    /// </summary>
    PatchReviewSnapshot GetSnapshot();

    /// <summary>Unified diff of one changed file against HEAD, or <c>null</c> when unavailable.</summary>
    string? GetFileDiff(string path);

    /// <summary>Current root-relative file content from the working tree, or <c>null</c> when unavailable.</summary>
    string? GetFileContent(string path);
}
