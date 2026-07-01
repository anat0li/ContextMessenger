using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ContextMessenger.App.Wpf.Services;
using ContextMessenger.Core.Patching;
using ContextMessenger.Protocol;
using ContextMessenger.Protocol.Review;

namespace ContextMessenger.App.Wpf.Patching;

/// <summary>
/// Live patch review actions for one loop: drives the patch service, submits the resulting
/// response to the chat target, and updates the held-interaction store. Invoked from the UI
/// thread by the review view-model; <paramref name="onChanged"/> lets the host refresh the
/// projected view-model from the store after each action.
/// </summary>
public sealed class HeldPatchActions : IHeldPatchActions
{
    private const string NoActivePatchStatus = "none";

    private readonly IPatchTransactionService _patches;
    private readonly ITargetAutomationAdapter _automation;
    private readonly IHeldPatchInteractionStore _store;
    private readonly IPatchDiffService _diff;
    private readonly string _rootPath;
    private readonly Action _onChanged;
    private readonly Action<string>? _onResponseProduced;

    public HeldPatchActions(
        IPatchTransactionService patches,
        ITargetAutomationAdapter automation,
        IHeldPatchInteractionStore store,
        IPatchDiffService diff,
        string rootPath,
        Action onChanged,
        Action<string>? onResponseProduced = null)
    {
        _patches = patches ?? throw new ArgumentNullException(nameof(patches));
        _automation = automation ?? throw new ArgumentNullException(nameof(automation));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _diff = diff ?? throw new ArgumentNullException(nameof(diff));
        _rootPath = Path.GetFullPath(rootPath ?? throw new ArgumentNullException(nameof(rootPath)));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        _onResponseProduced = onResponseProduced;
    }

    public async Task SendAsync(IReadOnlyList<ReviewerComment> comments)
    {
        var held = _store.Current;
        if (held is null) return;

        // Deliver the held verdict, injecting reviewer comments as a structured reviewerComments
        // array so the model can address them (and reply by id) in its next amend.
        var responseText = ReviewResponseAugmenter.Augment(held.HeldResponseText, comments);
        var submitted = await _automation.SubmitResponseAsync(responseText, CancellationToken.None);
        if (submitted)
            _onResponseProduced?.Invoke(responseText);

        _store.Save(held.WithPhase(PatchInteractionPhase.AwaitingModelReply));
        _onChanged();
    }

    public async Task AcceptAsync()
    {
        var held = _store.Current;
        if (held is null) return;

        var result = _patches.Accept(held.PatchId);
        var response = PatchResponseBuilder.Build(held.RequestId, held.CommandType, 0, result);
        var submitted = await _automation.SubmitResponseAsync(response, CancellationToken.None);
        if (submitted)
            _onResponseProduced?.Invoke(response);

        _store.Clear();
        _onChanged();
    }

    public async Task RevertAsync()
    {
        var held = _store.Current;
        if (held is null) return;

        var result = _patches.Revert(held.PatchId);
        var response = PatchResponseBuilder.Build(held.RequestId, held.CommandType, 0, result);
        var submitted = await _automation.SubmitResponseAsync(response, CancellationToken.None);
        if (submitted)
            _onResponseProduced?.Invoke(response);

        _store.Clear();
        _onChanged();
    }

    public Task RefreshAsync()
    {
        var held = _store.Current;
        if (held is null) return Task.CompletedTask;

        var current = _patches.Current();
        if (string.Equals(current.PatchStatus, NoActivePatchStatus, StringComparison.Ordinal))
            _store.Clear(); // patch gone (externally reverted/committed) — close the review
        else
            _store.Save(held.WithStatus(current.PatchStatus, current.Revision));

        _onChanged();
        return Task.CompletedTask;
    }

    public PatchReviewSnapshot GetSnapshot()
    {
        var current = _patches.Current();
        if (string.Equals(current.PatchStatus, NoActivePatchStatus, StringComparison.Ordinal))
            return PatchReviewSnapshot.Empty;

        return new PatchReviewSnapshot
        {
            Title = current.Title,
            Description = current.Description,
            CommitMessage = current.CommitMessage,
            PatchId = current.PatchId ?? "",
            Revision = current.Revision,
            Status = current.PatchStatus,
            Files = current.Files
                .Select(f => new PatchReviewFile { Path = f.Path, Operation = f.Operation })
                .ToArray(),
        };
    }

    public string? GetFileDiff(string path) =>
        string.IsNullOrEmpty(path) ? null : _diff.GetUnifiedDiff(path);

    public string? GetFileContent(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, path.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsUnderRoot(fullPath, _rootPath) || !File.Exists(fullPath))
            return null;

        return File.ReadAllText(fullPath);
    }

    private static bool IsUnderRoot(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullRoot, comparison);
    }
}
