using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContextMessenger.App.Wpf.Patching;
using ContextMessenger.Protocol.Dispatch;
using ContextMessenger.Protocol.Review;
using System.Globalization;

namespace ContextMessenger.App.Wpf.ViewModels;

/// <summary>
/// Projects the active <see cref="HeldPatchInteraction"/> for the review page and exposes
/// the three patch actions. Commands delegate to <see cref="IHeldPatchActions"/>; the
/// view-model owns no patch-service or chat-target behavior itself.
/// </summary>
public sealed partial class PatchReviewViewModel : ObservableObject
{
    private readonly IHeldPatchActions _actions;
    private readonly CommentAnchorResolver _anchorResolver = new();

    public PatchReviewViewModel(IHeldPatchActions actions)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        Comments.CollectionChanged += OnCommentsChanged;
    }

    /// <summary>Reviewer comments added on this patch, delivered to the model on Send.</summary>
    public ObservableCollection<ReviewComment> Comments { get; } = new();

    public event EventHandler? Changed;

    [ObservableProperty]
    private HeldPatchInteraction? _interaction;

    /// <summary>
    /// True only while the review tab is the current/selected tab. The patch actions are
    /// enabled exclusively when the reviewer is looking at the review page, so a held patch
    /// cannot be acted on from a log tab.
    /// </summary>
    [ObservableProperty]
    private bool _isActive;

    /// <summary>The changed file selected in the left tree; drives the diff view.</summary>
    [ObservableProperty]
    private PatchReviewFile? _selectedFile;

    /// <summary>All diff lines of <see cref="SelectedFile"/> (full file context) for the diff view.</summary>
    [ObservableProperty]
    private IReadOnlyList<DiffLine> _selectedFileDiff = [];

    /// <summary>New-file line to jump the diff caret to (e.g. from an error); null clears it.</summary>
    [ObservableProperty]
    private int? _selectedFileLine;

    /// <summary>Changed files of the active patch as a folder/file tree (left panel).</summary>
    [ObservableProperty]
    private IReadOnlyList<PatchTreeNode> _rootNodes = [];

    /// <summary>History rows; the last carries the held response for in-place expansion.</summary>
    [ObservableProperty]
    private IReadOnlyList<PatchHistoryRow> _historyRows = [];

    /// <summary>Build errors for the Errors tab (empty when the build passed/skipped).</summary>
    [ObservableProperty]
    private IReadOnlyList<BuildErrorRow> _buildErrors = [];

    /// <summary>Build warnings for the Warnings tab (empty when none were reported).</summary>
    [ObservableProperty]
    private IReadOnlyList<BuildWarningRow> _buildWarnings = [];

    /// <summary>Failed tests for the Tests tab (empty when tests passed/skipped).</summary>
    [ObservableProperty]
    private IReadOnlyList<TestFailureRow> _testFailures = [];

    /// <summary>Selected bottom-tab index; jumps to Errors/Tests when a build or test failed.</summary>
    [ObservableProperty]
    private int _detailTabIndex;

    private const int HistoryTabIndex = 0;
    private const int ErrorsTabIndex = 1;
    private const int WarningsTabIndex = 2;
    private const int TestsTabIndex = 3;
    private const int CommentsTabIndex = 4;

    private PatchReviewSnapshot _snapshot = PatchReviewSnapshot.Empty;

    // Patch id the current comments belong to; comments clear when the patch closes or changes.
    private string? _commentsPatchId;

    // Highest reply-turn whose model comment-replies have been applied (dedup across Refresh and
    // across reply-only amends that keep the revision unchanged).
    private int _appliedReplyTurn = -1;

    public bool HasInteraction => Interaction is not null;

    public bool HasBuildErrors => BuildErrors.Count > 0;

    public bool HasBuildWarnings => BuildWarnings.Count > 0;

    public bool HasTestFailures => TestFailures.Count > 0;

    public bool HasComments => Comments.Count > 0;

    public bool HasPendingComments => Comments.Any(c => c.Pending);

    public bool HasOpenIssues => Comments.Any(c => c.OpenIssue);

    public string PatchId => Interaction?.PatchId ?? "";

    public int Revision => Interaction?.Revision ?? 0;

    public string TransactionStatus => Interaction?.TransactionStatus ?? "";

    public string Phase => Interaction?.Phase.ToString() ?? "";

    public string HeldResponseText => Interaction?.HeldResponseText ?? "";

    public IReadOnlyList<PatchInteractionEntry> History => Interaction?.History ?? [];

    /// <summary>Short tab-strip title, e.g. <c>Review – needs_revision</c>.</summary>
    public string TabTitle => Interaction is null ? "Review" : $"Review – {TransactionStatus}";

    /// <summary>Validated patch (build + tests passed) — drives the green check on the tab.</summary>
    public bool IsValidated =>
        string.Equals(TransactionStatus, PatchTransactionStatuses.AwaitingAcceptance, StringComparison.Ordinal);

    /// <summary>Patch that failed build/tests — drives the red exclamation on the tab.</summary>
    public bool IsInvalid =>
        string.Equals(TransactionStatus, PatchTransactionStatuses.NeedsRevision, StringComparison.Ordinal);

    public string Summary => Interaction is null
        ? "No patch under review."
        : $"Patch {PatchId} · rev {Revision} · {TransactionStatus} · {Phase}";

    // Descriptive fields shown in the Info dialog, read from the live patch snapshot.
    public string Title => _snapshot.Title ?? "";

    public string Description => _snapshot.Description ?? "";

    public string CommitMessage => _snapshot.CommitMessage ?? "";

    public string BuildSummary => FormatStageSummary(Interaction?.BuildSummary, "Build");

    public string TestSummary => FormatStageSummary(Interaction?.TestSummary, "Tests");

    /// <summary>Replaces the projected interaction (null closes the page) and refreshes enablement.</summary>
    public void Update(HeldPatchInteraction? interaction)
    {
        if (EqualityComparer<HeldPatchInteraction?>.Default.Equals(Interaction, interaction))
        {
            RefreshSnapshot();
            NotifyCommandsCanExecuteChanged();
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        Interaction = interaction;
    }

    public void RestoreState(HeldReviewState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        ClearComments();
        foreach (var comment in state.Comments)
            Comments.Add(comment.ToComment());

        _commentsPatchId = state.Interaction.PatchId;
        _appliedReplyTurn = state.Interaction.ReplyTurn;
        Interaction = state.Interaction;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnInteractionChanged(HeldPatchInteraction? value)
    {
        OnPropertyChanged(nameof(HasInteraction));
        OnPropertyChanged(nameof(PatchId));
        OnPropertyChanged(nameof(Revision));
        OnPropertyChanged(nameof(TransactionStatus));
        OnPropertyChanged(nameof(Phase));
        OnPropertyChanged(nameof(HeldResponseText));
        OnPropertyChanged(nameof(History));
        OnPropertyChanged(nameof(TabTitle));
        OnPropertyChanged(nameof(IsValidated));
        OnPropertyChanged(nameof(IsInvalid));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(BuildSummary));
        OnPropertyChanged(nameof(TestSummary));
        OnPropertyChanged(nameof(HasOpenIssues));

        RefreshSnapshot();
        NotifyCommandsCanExecuteChanged();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Pull the changed files (as a tree) + descriptive fields + history from the live patch
    // service, and select the first file (which loads its diff). Cleared when no patch is under review.
    private void RefreshSnapshot()
    {
        var previousDetailTabIndex = DetailTabIndex;
        var previousSelectedPath = SelectedFile?.Path;
        var samePatch = string.Equals(Interaction?.PatchId, _commentsPatchId, StringComparison.Ordinal);
        _snapshot = Interaction is null ? PatchReviewSnapshot.Empty : _actions.GetSnapshot();

        // Comments belong to a single patch; drop them when the patch closes or a different one opens.
        if (!samePatch)
        {
            _commentsPatchId = Interaction?.PatchId;
            _appliedReplyTurn = -1;
            ClearComments();
        }

        // Append the model's replies (from a fresh amend) to their threads before rebuilding rows.
        ApplyCommentReplies();
        var anchorsChanged = ReanchorComments();

        RootNodes = PatchTreeBuilder.Build(_snapshot.Files);
        HistoryRows = BuildHistoryRows();
        BuildErrors = BuildErrorRows();
        BuildWarnings = BuildWarningRows();
        TestFailures = BuildTestFailureRows();

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(CommitMessage));
        OnPropertyChanged(nameof(BuildSummary));
        OnPropertyChanged(nameof(TestSummary));

        // Preserve the reviewer's current bottom tab across same-patch refresh/amend updates
        // while it still exists; new patches still auto-surface failures.
        DetailTabIndex = samePatch && IsDetailTabAvailable(previousDetailTabIndex)
            ? previousDetailTabIndex
            : DefaultDetailTabIndex();

        var selected = samePatch
            ? FindFileByPath(RootNodes, previousSelectedPath)
            : null;
        selected ??= FindFirstFile(RootNodes);
        if (selected is not null)
            selected.IsSelected = true;
        SelectNode(selected);

        NotifyCommandsCanExecuteChanged();

        if (anchorsChanged)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    private int DefaultDetailTabIndex() =>
        HasBuildErrors ? ErrorsTabIndex
            : HasTestFailures ? TestsTabIndex
            : HistoryTabIndex;

    private bool IsDetailTabAvailable(int index) => index switch
    {
        HistoryTabIndex => true,
        ErrorsTabIndex => HasBuildErrors,
        WarningsTabIndex => HasBuildWarnings,
        TestsTabIndex => HasTestFailures,
        CommentsTabIndex => HasComments,
        _ => false,
    };

    partial void OnBuildErrorsChanged(IReadOnlyList<BuildErrorRow> value) =>
        OnPropertyChanged(nameof(HasBuildErrors));

    partial void OnBuildWarningsChanged(IReadOnlyList<BuildWarningRow> value) =>
        OnPropertyChanged(nameof(HasBuildWarnings));

    partial void OnTestFailuresChanged(IReadOnlyList<TestFailureRow> value) =>
        OnPropertyChanged(nameof(HasTestFailures));

    private IReadOnlyList<BuildErrorRow> BuildErrorRows() =>
        (Interaction?.BuildErrors ?? [])
            .Select(error => new BuildErrorRow
            {
                Code = error.Code ?? "",
                Path = error.Path ?? "",
                Line = error.Line,
                Location = FormatLocation(error.Path, error.Line, error.Column),
                Message = error.Message,
            })
            .ToArray();

    private IReadOnlyList<BuildWarningRow> BuildWarningRows() =>
        (Interaction?.BuildWarnings ?? [])
            .Select(warning => new BuildWarningRow
            {
                Code = warning.Code ?? "",
                Path = warning.Path ?? "",
                Line = warning.Line,
                Location = FormatLocation(warning.Path, warning.Line, warning.Column),
                Message = warning.Message,
            })
            .ToArray();

    private IReadOnlyList<TestFailureRow> BuildTestFailureRows() =>
        (Interaction?.TestFailures ?? [])
            .Select(failure => new TestFailureRow
            {
                Name = string.IsNullOrEmpty(failure.Code) ? "Failed test" : failure.Code!,
                Path = failure.Path ?? "",
                Line = failure.Line,
                // Many runners report no source; HasLocation then suppresses the jump link.
                Location = string.IsNullOrEmpty(failure.Path) ? "" : FormatLocation(failure.Path, failure.Line, failure.Column),
                Message = failure.Message,
                // Jump only when the test source is part of the patch (e.g. a new test file).
                CanJump = FindMatchingFile(RootNodes, (failure.Path ?? "").Replace('\\', '/')) is not null,
            })
            .ToArray();

    private static string FormatLocation(string? path, int? line, int? column)
    {
        var basePath = path ?? "";
        if (line is null)
            return basePath;

        return column is null ? $"{basePath} ({line})" : $"{basePath} ({line},{column})";
    }

    private static string FormatStageSummary(PatchStageSummary? summary, string label)
    {
        if (summary is null || string.IsNullOrWhiteSpace(summary.Status))
            return $"{label}: not reported";

        var parts = new List<string> { $"{label}: {summary.Status}" };
        if (!string.IsNullOrWhiteSpace(summary.Policy))
            parts.Add($"policy {summary.Policy}");
        if (summary.DurationMs is int durationMs)
            parts.Add($"{durationMs.ToString(CultureInfo.InvariantCulture)} ms");
        if (summary.ExitCode is int exitCode)
            parts.Add($"exit {exitCode.ToString(CultureInfo.InvariantCulture)}");

        if (summary.TotalTests is not null ||
            summary.ExecutedTests is not null ||
            summary.PassedTests is not null ||
            summary.FailedTests is not null ||
            summary.SkippedTests is not null)
        {
            parts.Add(
                $"tests total {FormatNullable(summary.TotalTests)}, executed {FormatNullable(summary.ExecutedTests)}, " +
                $"passed {FormatNullable(summary.PassedTests)}, failed {FormatNullable(summary.FailedTests)}, skipped {FormatNullable(summary.SkippedTests)}");
        }

        return string.Join(" · ", parts);
    }

    private static string FormatNullable(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "n/a";

    /// <summary>Open the changed file associated with a build error in the diff panel, if one matches.</summary>
    [RelayCommand]
    private void OpenError(BuildErrorRow? error)
    {
        if (error is not null)
            JumpToFile(error.Path, error.Line);
    }

    /// <summary>Open the changed file associated with a build warning in the diff panel, if one matches.</summary>
    [RelayCommand]
    private void OpenWarning(BuildWarningRow? warning)
    {
        if (warning is not null)
            JumpToFile(warning.Path, warning.Line);
    }

    /// <summary>Jump to the test's source only when it is part of the patch (e.g. a new test).</summary>
    [RelayCommand]
    private void OpenTest(TestFailureRow? failure)
    {
        if (failure is not null)
            JumpToFile(failure.Path, failure.Line);
    }

    private void JumpToFile(string? path, int? line)
    {
        var match = FindMatchingFile(RootNodes, (path ?? "").Replace('\\', '/'));
        if (match is null)
            return;

        SetSelected(RootNodes, match);
        SelectNode(match);
        // Jump the diff caret to the line (set after SelectNode, which resets it).
        SelectedFileLine = line;
    }

    private static PatchTreeNode? FindMatchingFile(IReadOnlyList<PatchTreeNode> nodes, string normalizedErrorPath)
    {
        foreach (var node in nodes)
        {
            if (!node.IsFolder && node.RelativePath is { } rel && PathsAssociate(normalizedErrorPath, rel))
                return node;

            if (node.IsFolder && FindMatchingFile(node.Children, normalizedErrorPath) is { } match)
                return match;
        }

        return null;
    }

    // Best-effort: the diagnostic path may be absolute or project-relative, while the changed file
    // is repo-relative — accept an exact match, a path-suffix match either way, or a file-name match.
    private static bool PathsAssociate(string errorPath, string relativePath)
    {
        if (errorPath.Length == 0)
            return false;
        if (string.Equals(errorPath, relativePath, StringComparison.OrdinalIgnoreCase))
            return true;
        if (errorPath.EndsWith("/" + relativePath, StringComparison.OrdinalIgnoreCase))
            return true;
        if (relativePath.EndsWith("/" + errorPath, StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(Path.GetFileName(errorPath), Path.GetFileName(relativePath), StringComparison.OrdinalIgnoreCase);
    }

    private static void SetSelected(IReadOnlyList<PatchTreeNode> nodes, PatchTreeNode target)
    {
        foreach (var node in nodes)
        {
            node.IsSelected = ReferenceEquals(node, target);
            SetSelected(node.Children, target);
        }
    }

    private static PatchTreeNode? FindFirstFile(IReadOnlyList<PatchTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (!node.IsFolder)
                return node;

            if (FindFirstFile(node.Children) is { } file)
                return file;
        }

        return null;
    }

    private static PatchTreeNode? FindFileByPath(IReadOnlyList<PatchTreeNode> nodes, string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        foreach (var node in nodes)
        {
            if (!node.IsFolder &&
                node.RelativePath is { } relativePath &&
                string.Equals(relativePath, path, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            if (node.IsFolder && FindFileByPath(node.Children, path) is { } match)
                return match;
        }

        return null;
    }

    private IReadOnlyList<PatchHistoryRow> BuildHistoryRows()
    {
        var entries = Interaction?.History ?? [];
        var held = HeldResponseText;
        return entries
            .Select((entry, index) => new PatchHistoryRow
            {
                Direction = entry.Direction.ToString(),
                Summary = entry.Summary,
                Revision = entry.Revision,
                // Only the latest exchange carries the held response, revealed on expansion.
                HeldResponse = index == entries.Count - 1 ? held : "",
            })
            .ToArray();
    }

    /// <summary>Called by the tree view when selection changes; loads the diff for a file node.</summary>
    public void SelectNode(PatchTreeNode? node) =>
        SelectedFile = node is { IsFolder: false, RelativePath: { } path }
            ? new PatchReviewFile { Path = path, Operation = node.Operation }
            : null;

    partial void OnSelectedFileChanged(PatchReviewFile? value)
    {
        SelectedFileLine = null;
        SelectedFileDiff = value is null
            ? []
            : UnifiedDiffParser.Parse(_actions.GetFileDiff(value.Path))
                .Where(line => line.Kind is DiffLineKind.Added or DiffLineKind.Removed or DiffLineKind.Context)
                .ToArray();
    }

    partial void OnIsActiveChanged(bool value) => NotifyCommandsCanExecuteChanged();

    private void NotifyCommandsCanExecuteChanged()
    {
        SendCommand.NotifyCanExecuteChanged();
        AcceptCommand.NotifyCanExecuteChanged();
        RevertCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
    }

    private bool IsReviewing =>
        Interaction is not null && Interaction.Phase == PatchInteractionPhase.Reviewing;

    // Send: deliver the held verdict while the human holds the floor — but only when there is
    // something to say (the patch is not validated, or there is an unsent reviewer comment).
    private bool CanSend() => IsActive && IsReviewing && (!IsValidated || HasPendingComments);

    // True when the patch still has changed files; a fix can revert the tree to clean, leaving
    // an awaiting_acceptance patch with nothing to accept.
    private bool HasChangedFiles => _snapshot.Files.Count > 0;

    // Accept: terminal approval, only for a validated (awaiting-acceptance) patch that still has
    // changes. Revert and Send stay available so the reviewer can cancel or ask the model.
    private bool CanAccept() =>
        IsActive &&
        HasChangedFiles &&
        !HasOpenIssues &&
        Interaction is not null &&
        string.Equals(Interaction.TransactionStatus, PatchTransactionStatuses.AwaitingAcceptance, StringComparison.Ordinal);

    // Revert / Refresh: available for any active patch, but only from the review tab.
    private bool CanActOnPatch() => IsActive && HasInteraction;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task Send()
    {
        // Deliver only comments with an unsent reviewer message; clear their pending flag after.
        var pending = Comments.Where(c => c.Pending).ToArray();
        var comments = pending
            .Select(c => new ReviewerComment { Id = c.Id, Path = c.Path, Line = c.Line, Comment = c.LatestReviewerText, OpenIssue = c.OpenIssue })
            .ToArray();

        await _actions.SendAsync(comments);

        foreach (var comment in pending)
            comment.Pending = false;
        SendCommand.NotifyCanExecuteChanged();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Adds a reviewer comment anchored to the current file and the given line.</summary>
    public void AddComment(int line, string text, bool openIssue = false)
    {
        if (SelectedFile is null || string.IsNullOrWhiteSpace(text))
            return;

        var comment = new ReviewComment
        {
            Id = NewCommentId(),
            Path = SelectedFile.Path,
            Line = line,
            Pending = true,
            OpenIssue = openIssue,
        };
        var anchor = _anchorResolver.Capture(SelectedFile.Path, line, _actions.GetFileContent(SelectedFile.Path));
        comment.AnchorText = anchor.AnchorText;
        comment.BeforeContext = anchor.BeforeContext;
        comment.AfterContext = anchor.AfterContext;
        comment.Messages.Add(new CommentMessage(CommentAuthor.Reviewer, ReviewerLabel, text.Trim()));
        Comments.Add(comment);
        DetailTabIndex = CommentsTabIndex;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool ReanchorComments()
    {
        if (Comments.Count == 0)
            return false;

        return _anchorResolver.Reanchor(
            Comments,
            _snapshot.Files,
            path => _actions.GetFileContent(path));
    }

    /// <summary>Appends another reviewer message to an existing comment thread (same id).</summary>
    public void RespondToComment(ReviewComment? comment, string text, bool resolveIssue = false)
    {
        if (comment is null || string.IsNullOrWhiteSpace(text))
            return;

        comment.Messages.Add(new CommentMessage(CommentAuthor.Reviewer, ReviewerLabel, text.Trim()));
        comment.Pending = true;
        if (resolveIssue)
            comment.OpenIssue = false;
        DetailTabIndex = CommentsTabIndex;
        SendCommand.NotifyCanExecuteChanged();
        AcceptCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasOpenIssues));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Append the model's replies (from the last amend) to their comment threads, once per amend.
    private void ApplyCommentReplies()
    {
        if (Interaction is not { } interaction)
        {
            _appliedReplyTurn = -1;
            return;
        }

        if (interaction.ReplyTurn <= _appliedReplyTurn)
            return;

        _appliedReplyTurn = interaction.ReplyTurn;
        // The model side is labelled with the chat target's name (e.g. "ChatGPT"), not "Model".
        var modelLabel = string.IsNullOrEmpty(interaction.TargetName) ? "Model" : interaction.TargetName;
        foreach (var reply in interaction.CommentReplies)
        {
            var comment = Comments.FirstOrDefault(c => string.Equals(c.Id, reply.Id, StringComparison.Ordinal));
            if (comment is null)
            {
                comment = CreateModelComment(reply);
                Comments.Add(comment);
            }
            else
            {
                comment.Messages.Add(new CommentMessage(CommentAuthor.Model, modelLabel, reply.Reply));
                if (reply.OpenIssue is bool openIssue)
                    comment.OpenIssue = openIssue;
            }
        }

        OnPropertyChanged(nameof(HasComments));
        OnPropertyChanged(nameof(HasOpenIssues));
        AcceptCommand.NotifyCanExecuteChanged();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private ReviewComment CreateModelComment(PatchCommentReply reply)
    {
        var path = reply.Path ?? "";
        var line = string.IsNullOrWhiteSpace(path) ? 0 : Math.Max(0, reply.Line);
        var comment = new ReviewComment
        {
            Id = reply.Id,
            Path = path,
            Line = line,
            Pending = false,
            OpenIssue = reply.OpenIssue ?? false,
        };

        if (comment.HasAnchor)
        {
            var anchor = _anchorResolver.Capture(path, line, _actions.GetFileContent(path));
            comment.AnchorText = anchor.AnchorText;
            comment.BeforeContext = anchor.BeforeContext;
            comment.AfterContext = anchor.AfterContext;
        }

        var modelLabel = string.IsNullOrEmpty(Interaction?.TargetName) ? "Model" : Interaction.TargetName;
        comment.Messages.Add(new CommentMessage(CommentAuthor.Model, modelLabel, reply.Reply));
        return comment;
    }

    [RelayCommand]
    private void RemoveComment(ReviewComment? comment)
    {
        if (comment is not null)
        {
            Comments.Remove(comment);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void OpenComment(ReviewComment? comment)
    {
        if (comment is { HasAnchor: true })
            JumpToFile(comment.Path, comment.Line);
    }

    private void OnCommentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (ReviewComment comment in e.OldItems)
                comment.PropertyChanged -= OnCommentPropertyChanged;
        }

        if (e.NewItems is not null)
        {
            foreach (ReviewComment comment in e.NewItems)
                comment.PropertyChanged += OnCommentPropertyChanged;
        }

        OnPropertyChanged(nameof(HasComments));
        OnPropertyChanged(nameof(HasOpenIssues));
        SendCommand.NotifyCanExecuteChanged();
        AcceptCommand.NotifyCanExecuteChanged();
    }

    private void ClearComments()
    {
        foreach (var comment in Comments)
            comment.PropertyChanged -= OnCommentPropertyChanged;

        Comments.Clear();
    }

    private void OnCommentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ReviewComment.OpenIssue))
            return;

        OnPropertyChanged(nameof(HasOpenIssues));
        AcceptCommand.NotifyCanExecuteChanged();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private const string ReviewerLabel = "You";

    private static string NewCommentId() => "c-" + Guid.NewGuid().ToString("N")[..8];

    [RelayCommand(CanExecute = nameof(CanAccept))]
    private Task Accept() => _actions.AcceptAsync();

    [RelayCommand(CanExecute = nameof(CanActOnPatch))]
    private Task Revert() => _actions.RevertAsync();

    [RelayCommand(CanExecute = nameof(CanActOnPatch))]
    private Task Refresh() => _actions.RefreshAsync();
}
