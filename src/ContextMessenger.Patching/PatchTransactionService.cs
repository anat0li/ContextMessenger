using ContextMessenger.Core.Patching;
using ContextMessenger.Core.Roslyn;
using ContextMessenger.FileSystem;
using LibGit2Sharp;

namespace ContextMessenger.Patching;

public sealed class PatchTransactionService : IPatchTransactionService
{
    private readonly string _rootPath;
    private readonly string _rootName;
    private readonly IPatchSessionStore? _store;
    private readonly IBuildRunner _buildRunner;
    private readonly ITestRunner _testRunner;
    private readonly IRoslynWorkspaceInvalidator? _workspaceInvalidator;
    private readonly IRoslynNavigationService? _roslynNavigation;
    private readonly IPatchDiffVerifier _diffVerifier;
    private readonly FilePatchApplier _applier;
    private readonly LibGit2SharpGitStatusService _gitStatus;
    private ActivePatch? _active;
    private PatchSessionMetadata? _foreignActive;

    // Serializes every public operation. The host dispatches propose/amend on the loop's
    // background thread while accept/revert/current arrive from the review UI thread; without
    // this gate those interleave on _active/_store and corrupt transaction state.
    private readonly object _gate = new();

    // Deferred-acceptance state: applied + checks passed, but not staged and not closed.
    // The transaction stays open until an explicit Accept (stage + close) or Revert.
    private const string AwaitingAcceptanceStatus = "awaiting_acceptance";

    // When set (per-root hold-for-review policy), passing patches defer acceptance even
    // when the request itself did not ask to. OR-combined with request.DeferAcceptance.
    // Settable so the host can flip hold-for-review at runtime. Kept off _gate (and volatile)
    // so toggling the policy never blocks behind an in-flight build/test.
    private volatile bool _deferAcceptanceByDefault;
    public bool DeferAcceptanceByDefault
    {
        get => _deferAcceptanceByDefault;
        set => _deferAcceptanceByDefault = value;
    }

    public PatchTransactionService(string rootPath)
        : this(rootPath, rootName: Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath))))
    {
    }

    public PatchTransactionService(
        string rootPath,
        string rootName,
        IPatchSessionStore? store = null,
        IBuildRunner? buildRunner = null,
        ITestRunner? testRunner = null,
        IRoslynWorkspaceInvalidator? workspaceInvalidator = null,
        IRoslynNavigationService? roslynNavigation = null,
        IPatchDiffVerifier? diffVerifier = null,
        bool deferAcceptanceByDefault = false)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Root path is required.", nameof(rootPath));

        _rootPath = Path.GetFullPath(rootPath);
        _rootName = string.IsNullOrWhiteSpace(rootName) ? _rootPath : rootName;
        _store = store;
        DeferAcceptanceByDefault = deferAcceptanceByDefault;
        _buildRunner = buildRunner ?? new DotnetBuildRunner(_rootPath);
        _testRunner = testRunner ?? new DotnetTestRunner(_rootPath);
        _workspaceInvalidator = workspaceInvalidator ?? roslynNavigation;
        _roslynNavigation = roslynNavigation ?? workspaceInvalidator as IRoslynNavigationService;
        _diffVerifier = diffVerifier ?? new DefaultPatchDiffVerifier();
        var sandbox = new PathSandbox(_rootPath);
        _applier = new FilePatchApplier(sandbox);
        _gitStatus = new LibGit2SharpGitStatusService(_rootPath);
        RecoverFromStore();
    }

    public bool HasActivePatch
    {
        get { lock (_gate) return _active is not null || _foreignActive is not null; }
    }

    public PatchTransactionResult Propose(ProposePatchRequest request)
    {
        lock (_gate)
            return ProposeCore(request);
    }

    private PatchTransactionResult ProposeCore(ProposePatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        SyncForeignActiveFromStore();
        if (HasActivePatch)
            throw new PatchValidationException("patch_in_progress", $"Patch '{(_active?.PatchId ?? _foreignActive?.PatchId)}' is already active.");

        if (request.Files.Count == 0 && request.Edits.Count == 0)
            throw new PatchValidationException("invalid_parameters", "propose_patch requires at least one file operation or edit.");

        var repoPath = Repository.Discover(_rootPath)
            ?? throw new PatchValidationException("not_git_repository", "propose_patch requires the active root to be inside a git repository.");

        using var repo = new Repository(repoPath);
        var baseHeadSha = repo.Head.Tip?.Sha
            ?? throw new PatchValidationException("invalid_git_state", "propose_patch requires a valid HEAD commit.");

        var before = _gitStatus.GetStatus();
        if (!before.IsClean)
            throw new PatchValidationException("dirty_working_tree", "propose_patch requires a clean git working tree.");

        var normalized = NormalizePatchOperations(request.Files, request.Edits);
        var normalizedOps = normalized.Operations;
        var createdPathsToRemoveOnRollback = CreatedPathsThatAreAbsentBeforeApply(normalizedOps);
        try
        {
            _applier.Apply(normalizedOps);

            var after = _gitStatus.GetStatus();
            _diffVerifier.Verify(normalizedOps, after.ChangedFiles);
            InvalidateWorkspace();
        }
        catch
        {
            ResetToBaseAndRemoveCreates(
                repoPath,
                baseHeadSha,
                createdPathsToRemoveOnRollback);
            throw;
        }

        var patch = new ActivePatch(
            PatchId: "p-" + Guid.NewGuid().ToString("N"),
            Status: "accepted",
            Revision: 1,
            Title: request.Title,
            Description: request.Description,
            CommitMessage: request.CommitMessage,
            BaseHeadSha: baseHeadSha,
            LastFailureStage: null,
            Recovered: false,
            BuildPolicy: request.Build,
            TestPolicy: request.Tests,
            Files: normalizedOps.Select(op => new ActivePatchFile(
                Path: op.Path,
                Operation: op.Operation.ToString().ToLowerInvariant(),
                OldContentHash: op.OldContentHash,
                LastRevision: 1)).ToArray());

        var build = RunBuild(request.Build);
        if (build.Status is "failed" or "timeout")
        {
            patch = patch with { Status = "needs_revision", LastFailureStage = "build", LastBuild = build, LastTests = SkippedStage(request.Tests) };
            _active = patch;
            _store?.Save(ToMetadata(patch, request.Build, request.Tests));
            return ToResult(
                patch,
                patchStatus: "needs_revision",
                applied: true,
                diffVerified: true,
                build,
                SkippedStage(request.Tests),
                normalized.Warnings);
        }

        var tests = RunTests(request.Tests);
        if (tests.Status is "failed" or "timeout")
        {
            patch = patch with { Status = "needs_revision", LastFailureStage = "tests", LastBuild = build, LastTests = tests };
            _active = patch;
            _store?.Save(ToMetadata(patch, request.Build, request.Tests));
            return ToResult(
                patch,
                patchStatus: "needs_revision",
                applied: true,
                diffVerified: true,
                build,
                tests,
                normalized.Warnings);
        }

        if (request.DeferAcceptance || DeferAcceptanceByDefault)
            return HoldForAcceptance(patch, request.Build, request.Tests, build, tests, normalized.Warnings);

        StageAcceptedPatch(repoPath, normalizedOps);
        _store?.Clear();
        return ToResult(patch, patchStatus: "accepted", applied: true, diffVerified: true, build, tests, normalized.Warnings);
    }

    public PatchTransactionResult Amend(AmendPatchRequest request)
    {
        lock (_gate)
            return AmendCore(request);
    }

    private PatchTransactionResult AmendCore(AmendPatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        SyncForeignActiveFromStore();
        if (_active is null)
        {
            if (_foreignActive is not null)
                throw new PatchValidationException("patch_in_progress", $"Patch '{_foreignActive.PatchId}' is active for root '{_foreignActive.RootName}'. Switch to that root before amending.");

            throw new PatchValidationException("patch_not_active", "No active patch exists.");
        }

        if (_active.Status is not ("needs_revision" or AwaitingAcceptanceStatus))
            throw new PatchValidationException("invalid_patch_state", "amend_patch requires an active patch in needs_revision or awaiting_acceptance state.");
        if (string.IsNullOrWhiteSpace(request.PatchId))
            throw new PatchValidationException("invalid_parameters", "patchId is required.");
        if (!string.Equals(_active.PatchId, request.PatchId, StringComparison.Ordinal))
            throw new PatchValidationException("patch_id_mismatch", $"Patch '{request.PatchId}' is not the active patch.");
        if (request.BaseRevision != _active.Revision)
            throw new PatchValidationException("revision_mismatch", $"amend_patch baseRevision {request.BaseRevision} does not match active revision {_active.Revision}.");

        // Reply-only amend (no file operations): carries the model's commentReplies (extracted by
        // the dispatcher) without re-applying, re-building, or re-testing. The patch keeps its
        // current state and revision so the model's baseRevision stays valid across reply turns.
        if (request.Files.Count == 0 && request.Edits.Count == 0)
        {
            _store?.Save(ToMetadata(_active, _active.BuildPolicy, _active.TestPolicy));
            return ToResult(
                _active,
                patchStatus: _active.Status,
                applied: true,
                diffVerified: true,
                _active.LastBuild ?? SkippedStage(_active.BuildPolicy),
                _active.LastTests ?? SkippedStage(_active.TestPolicy));
        }

        var buildPolicy = request.Build ?? _active.BuildPolicy;
        var testsPolicy = request.Tests ?? _active.TestPolicy;

        var repoPath = Repository.Discover(_rootPath)
            ?? throw new PatchValidationException("not_git_repository", "Active patch root is no longer inside a git repository.");

        var before = _gitStatus.GetStatus();
        if (before.IsClean)
            throw new PatchValidationException("invalid_git_state", "Active patch metadata exists, but the git working tree is clean.");

        var normalized = NormalizePatchOperations(request.Files, request.Edits);
        var normalizedOps = normalized.Operations;
        var preAmendSnapshot = SnapshotWorkingTree(before.ChangedFiles.Select(f => f.Path).Concat(normalizedOps.Select(op => op.Path)));
        IReadOnlyList<ActivePatchFile> amendedFiles;
        try
        {
            _applier.Apply(normalizedOps);

            var after = _gitStatus.GetStatus();
            amendedFiles = MergeFiles(_active.Files, normalizedOps, _active.Revision + 1, after.ChangedFiles);
            _diffVerifier.Verify(amendedFiles.Select(ToPatchFileOperation).ToArray(), after.ChangedFiles);
            InvalidateWorkspace();
        }
        catch
        {
            RestoreWorkingTreeSnapshot(preAmendSnapshot);
            throw;
        }

        var patch = _active with
        {
            Status = "accepted",
            Revision = _active.Revision + 1,
            Description = request.Description ?? _active.Description,
            LastFailureStage = null,
            BuildPolicy = buildPolicy,
            TestPolicy = testsPolicy,
            Files = amendedFiles,
        };

        var build = RunBuild(buildPolicy);
        if (build.Status is "failed" or "timeout")
        {
            patch = patch with { Status = "needs_revision", LastFailureStage = "build", LastBuild = build, LastTests = SkippedStage(testsPolicy) };
            _active = patch;
            _store?.Save(ToMetadata(patch, buildPolicy, testsPolicy));
            return ToResult(
                patch,
                patchStatus: "needs_revision",
                applied: true,
                diffVerified: true,
                build,
                SkippedStage(testsPolicy),
                normalized.Warnings);
        }

        var tests = RunTests(testsPolicy);
        if (tests.Status is "failed" or "timeout")
        {
            patch = patch with { Status = "needs_revision", LastFailureStage = "tests", LastBuild = build, LastTests = tests };
            _active = patch;
            _store?.Save(ToMetadata(patch, buildPolicy, testsPolicy));
            return ToResult(
                patch,
                patchStatus: "needs_revision",
                applied: true,
                diffVerified: true,
                build,
                tests,
                normalized.Warnings);
        }

        if (request.DeferAcceptance || DeferAcceptanceByDefault)
            return HoldForAcceptance(patch, buildPolicy, testsPolicy, build, tests, normalized.Warnings);

        StageAcceptedPatch(repoPath, patch.Files.Select(ToPatchFileOperation).ToArray());
        _active = null;
        _store?.Clear();
        return ToResult(patch, patchStatus: "accepted", applied: true, diffVerified: true, build, tests, normalized.Warnings);
    }

    public PatchValidationResult Validate(ValidatePatchRequest request)
    {
        lock (_gate)
            return ValidateCore(request);
    }

    private PatchValidationResult ValidateCore(ValidatePatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        SyncForeignActiveFromStore();

        if (request.Files.Count == 0 && request.Edits.Count == 0)
            throw new PatchValidationException("invalid_parameters", "validate_patch requires at least one file operation or edit.");

        var isAmend = !string.IsNullOrWhiteSpace(request.PatchId) || request.BaseRevision.HasValue;
        var repoPath = Repository.Discover(_rootPath);
        if (repoPath is null)
            throw new PatchValidationException("not_git_repository", "validate_patch requires the active root to be inside a git repository.");

        using var repo = new Repository(repoPath);
        if (repo.Head.Tip is null)
            throw new PatchValidationException("invalid_git_state", "validate_patch requires a valid HEAD commit.");

        PatchPolicy buildPolicy;
        PatchPolicy testsPolicy;
        List<PatchWarning>? warnings = null;

        if (isAmend)
        {
            if (_active is null)
            {
                if (_foreignActive is not null)
                    throw new PatchValidationException("patch_in_progress", $"Patch '{_foreignActive.PatchId}' is active for root '{_foreignActive.RootName}'. Switch to that root before validating an amendment.");

                throw new PatchValidationException("patch_not_active", "No active patch exists.");
            }

            if (!string.Equals(_active.Status, "needs_revision", StringComparison.Ordinal))
                throw new PatchValidationException("invalid_patch_state", "validate_patch amendment mode requires an active patch in needs_revision state.");
            if (string.IsNullOrWhiteSpace(request.PatchId))
                throw new PatchValidationException("invalid_parameters", "patchId is required for validate_patch amendment mode.");
            if (!string.Equals(_active.PatchId, request.PatchId, StringComparison.Ordinal))
                throw new PatchValidationException("patch_id_mismatch", $"Patch '{request.PatchId}' is not the active patch.");
            if (!request.BaseRevision.HasValue)
                throw new PatchValidationException("invalid_parameters", "baseRevision is required for validate_patch amendment mode.");
            if (request.BaseRevision.Value != _active.Revision)
                throw new PatchValidationException("revision_mismatch", $"validate_patch baseRevision {request.BaseRevision.Value} does not match active revision {_active.Revision}.");

            var before = _gitStatus.GetStatus();
            if (before.IsClean)
                throw new PatchValidationException("invalid_git_state", "Active patch metadata exists, but the git working tree is clean.");

            buildPolicy = request.Build ?? _active.BuildPolicy;
            testsPolicy = request.Tests ?? _active.TestPolicy;
        }
        else
        {
            if (HasActivePatch)
                throw new PatchValidationException("patch_in_progress", $"Patch '{(_active?.PatchId ?? _foreignActive?.PatchId)}' is already active.");

            var before = _gitStatus.GetStatus();
            if (!before.IsClean)
            {
                warnings =
                [
                    new PatchWarning
                    {
                        Code = "dirty_working_tree",
                        Message = "validate_patch proposal mode validated against the current dirty working tree. propose_patch still requires a clean git working tree.",
                    },
                ];
            }

            buildPolicy = request.Build ?? new PatchPolicy();
            testsPolicy = request.Tests ?? new PatchPolicy();
        }

        var normalized = NormalizePatchOperations(request.Files, request.Edits);
        if (warnings is not null)
            warnings.AddRange(normalized.Warnings);

        _applier.Validate(normalized.Operations);
        ValidateBuildPolicy(buildPolicy);
        ValidateTestsPolicy(testsPolicy);

        return new PatchValidationResult
        {
            Valid = true,
            Mode = isAmend ? "amend" : "propose",
            PatchId = isAmend ? _active!.PatchId : null,
            BaseRevision = isAmend ? _active!.Revision : null,
            Applied = false,
            DiffVerified = false,
            Build = ValidatedStage(buildPolicy),
            Tests = ValidatedStage(testsPolicy),
            Warnings = warnings ?? normalized.Warnings,
            Files = normalized.Operations.Select(ToValidationFileState).ToArray(),
        };
    }

    public PatchTransactionResult Current()
    {
        lock (_gate)
            return CurrentCore();
    }

    private PatchTransactionResult CurrentCore()
    {
        if (_active is null)
        {
            if (_foreignActive is not null)
                return MetadataOnlyResult(_foreignActive, recovered: true);

            return new PatchTransactionResult { PatchStatus = "none" };
        }

        return ToResult(_active, patchStatus: _active.Status, applied: true, diffVerified: true, _active.LastBuild ?? SkippedStage(_active.BuildPolicy), _active.LastTests ?? SkippedStage(_active.TestPolicy));
    }

    public PatchTransactionResult Revert(string patchId)
    {
        lock (_gate)
            return RevertCore(patchId);
    }

    private PatchTransactionResult RevertCore(string patchId)
    {
        SyncForeignActiveFromStore();
        if (_active is null)
        {
            if (_foreignActive is not null)
                throw new PatchValidationException("patch_in_progress", $"Patch '{_foreignActive.PatchId}' is active for root '{_foreignActive.RootName}'. Switch to that root before reverting.");

            throw new PatchValidationException("patch_not_active", "No active patch exists.");
        }
        if (string.IsNullOrWhiteSpace(patchId))
            throw new PatchValidationException("invalid_parameters", "patchId is required.");
        if (!string.Equals(_active.PatchId, patchId, StringComparison.Ordinal))
            throw new PatchValidationException("patch_id_mismatch", $"Patch '{patchId}' is not the active patch.");

        var patch = _active;
        var repoPath = Repository.Discover(_rootPath)
            ?? throw new PatchValidationException("not_git_repository", "Active patch root is no longer inside a git repository.");

        // Reverting hard-resets the working tree to the patch base. If HEAD has moved off the
        // base commit since the patch was proposed (e.g. the user committed outside the app),
        // a hard reset would silently discard those intervening commits. Refuse instead.
        EnsureHeadMatchesBase(repoPath, patch.BaseHeadSha);

        ResetToBaseAndRemoveCreates(repoPath, patch.BaseHeadSha, patch.Files
            .Where(f => f.Operation == "create")
            .Select(f => f.Path)
            .ToArray());

        var status = _gitStatus.GetStatus();
        if (!status.IsClean)
            throw new PatchValidationException("revert_failed", "Patch revert completed but the git working tree is still dirty.");

        InvalidateWorkspace();
        _active = null;
        _store?.Clear();
        return new PatchTransactionResult
        {
            PatchStatus = "reverted",
            PatchId = patch.PatchId,
            Revision = patch.Revision,
            Title = patch.Title,
            Description = patch.Description,
            CommitMessage = patch.CommitMessage,
            Recovered = patch.Recovered,
            LastFailureStage = patch.LastFailureStage,
            Applied = false,
            DiffVerified = false,
            Files = patch.Files.Select(f => new PatchFileState
            {
                Path = f.Path,
                Operation = f.Operation,
                OldContentHash = f.OldContentHash,
                LastRevision = f.LastRevision,
            }).ToArray(),
        };
    }

    /// <summary>
    /// Finalizes a deferred (<c>awaiting_acceptance</c>) patch: stages the applied files and
    /// closes the transaction. This is the only place staging happens when acceptance was
    /// deferred; it mirrors the inline stage+close that the non-deferred path performs.
    /// </summary>
    public PatchTransactionResult Accept(string patchId)
    {
        lock (_gate)
            return AcceptCore(patchId);
    }

    private PatchTransactionResult AcceptCore(string patchId)
    {
        SyncForeignActiveFromStore();
        if (_active is null)
        {
            if (_foreignActive is not null)
                throw new PatchValidationException("patch_in_progress", $"Patch '{_foreignActive.PatchId}' is active for root '{_foreignActive.RootName}'. Switch to that root before accepting.");

            throw new PatchValidationException("patch_not_active", "No active patch exists.");
        }
        if (string.IsNullOrWhiteSpace(patchId))
            throw new PatchValidationException("invalid_parameters", "patchId is required.");
        if (!string.Equals(_active.PatchId, patchId, StringComparison.Ordinal))
            throw new PatchValidationException("patch_id_mismatch", $"Patch '{patchId}' is not the active patch.");
        if (!string.Equals(_active.Status, AwaitingAcceptanceStatus, StringComparison.Ordinal))
            throw new PatchValidationException("invalid_patch_state", "accept requires an active patch in awaiting_acceptance state.");

        var patch = _active;
        var repoPath = Repository.Discover(_rootPath)
            ?? throw new PatchValidationException("not_git_repository", "Active patch root is no longer inside a git repository.");

        StageAcceptedPatch(repoPath, patch.Files.Select(ToPatchFileOperation).ToArray());
        _active = null;
        _store?.Clear();
        // Report the build/test results that validated the patch, not "skipped".
        return ToResult(
            patch,
            patchStatus: "accepted",
            applied: true,
            diffVerified: true,
            patch.LastBuild ?? SkippedStage(patch.BuildPolicy),
            patch.LastTests ?? SkippedStage(patch.TestPolicy));
    }

    private PatchTransactionResult HoldForAcceptance(
        ActivePatch patch,
        PatchPolicy buildPolicy,
        PatchPolicy testsPolicy,
        PatchStageResult build,
        PatchStageResult tests,
        IReadOnlyList<PatchWarning> warnings)
    {
        var held = patch with
        {
            Status = AwaitingAcceptanceStatus,
            LastFailureStage = null,
            LastBuild = build,
            LastTests = tests,
        };
        _active = held;
        _store?.Save(ToMetadata(held, buildPolicy, testsPolicy));
        return ToResult(held, patchStatus: AwaitingAcceptanceStatus, applied: true, diffVerified: true, build, tests, warnings);
    }

    private static void EnsureHeadMatchesBase(string repoPath, string baseHeadSha)
    {
        using var repo = new Repository(repoPath);
        var headSha = repo.Head.Tip?.Sha
            ?? throw new PatchValidationException("invalid_git_state", "Active patch root has no valid HEAD commit.");
        if (!string.Equals(headSha, baseHeadSha, StringComparison.Ordinal))
            throw new PatchValidationException(
                "invalid_git_state",
                $"Cannot revert: HEAD ({Short(headSha)}) has moved off the patch base commit ({Short(baseHeadSha)}). " +
                "Reverting would discard intervening commits; resolve the divergence manually.");
    }

    private static string Short(string sha) => sha.Length <= 8 ? sha : sha[..8];

    private void ResetToBaseAndRemoveCreates(string repoPath, string baseHeadSha, IReadOnlyList<string> createdOrTouchedPaths)
    {
        using (var repo = new Repository(repoPath))
        {
            var commit = repo.Lookup<Commit>(baseHeadSha)
                ?? throw new PatchValidationException("invalid_git_state", $"Base commit '{baseHeadSha}' was not found.");
            repo.Reset(ResetMode.Hard, commit);
        }

        foreach (var path in createdOrTouchedPaths)
        {
            var abs = Path.GetFullPath(Path.Combine(_rootPath, path.Replace('/', Path.DirectorySeparatorChar)));
            if (IsUnderRoot(abs, _rootPath) && File.Exists(abs))
                File.Delete(abs);
        }
    }

    private void StageAcceptedPatch(string repoPath, IReadOnlyList<PatchFileOperation> operations)
    {
        using var repo = new Repository(repoPath);
        foreach (var path in operations.Select(op => op.Path).Distinct(StringComparer.OrdinalIgnoreCase))
            Commands.Stage(repo, path);
    }

    private void InvalidateWorkspace() => _workspaceInvalidator?.InvalidateWorkspace();

    private IReadOnlyList<FileSnapshot> SnapshotWorkingTree(IEnumerable<string> paths)
    {
        return paths
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var abs = Path.GetFullPath(Path.Combine(_rootPath, path.Replace('/', Path.DirectorySeparatorChar)));
                if (!IsUnderRoot(abs, _rootPath))
                    throw new PatchValidationException("path_outside_sandbox", $"Patch path is outside the root: {path}");

                return File.Exists(abs)
                    ? new FileSnapshot(path, File.ReadAllBytes(abs))
                    : new FileSnapshot(path, Content: null);
            })
            .ToArray();
    }

    private void RestoreWorkingTreeSnapshot(IReadOnlyList<FileSnapshot> snapshot)
    {
        foreach (var file in snapshot)
        {
            var abs = Path.GetFullPath(Path.Combine(_rootPath, file.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsUnderRoot(abs, _rootPath))
                continue;

            if (file.Content is null)
            {
                if (File.Exists(abs))
                    File.Delete(abs);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
            File.WriteAllBytes(abs, file.Content);
        }
    }

    private PatchTransactionResult ToResult(
        ActivePatch patch,
        string patchStatus,
        bool applied,
        bool diffVerified,
        PatchStageResult build,
        PatchStageResult tests,
        IReadOnlyList<PatchWarning>? warnings = null) =>
        new()
        {
            PatchStatus = patchStatus,
            PatchId = patch.PatchId,
            Revision = patch.Revision,
            Title = patch.Title,
            Description = patch.Description,
            CommitMessage = patch.CommitMessage,
            Recovered = patch.Recovered,
            LastFailureStage = patch.LastFailureStage,
            Applied = applied,
            DiffVerified = diffVerified,
            Build = build,
            Tests = tests,
            Warnings = warnings ?? [],
            Files = patch.Files.Select(ToFileState).ToArray(),
        };

    private PatchFileState ToFileState(ActivePatchFile file)
    {
        var abs = Path.GetFullPath(Path.Combine(_rootPath, file.Path.Replace('/', Path.DirectorySeparatorChar)));
        return new PatchFileState
        {
            Path = file.Path,
            Operation = file.Operation,
            OldContentHash = file.OldContentHash,
            CurrentContentHash = File.Exists(abs) ? ContentHash.ForFile(abs) : null,
            LastRevision = file.LastRevision,
        };
    }

    private PatchFileState ToValidationFileState(PatchFileOperation op)
    {
        var abs = Path.GetFullPath(Path.Combine(_rootPath, op.Path.Replace('/', Path.DirectorySeparatorChar)));
        return new PatchFileState
        {
            Path = op.Path,
            Operation = op.Operation.ToString().ToLowerInvariant(),
            OldContentHash = op.OldContentHash,
            CurrentContentHash = File.Exists(abs) ? ContentHash.ForFile(abs) : null,
            LastRevision = 0,
        };
    }

    private static PatchFileOperation NormalizeOperation(PatchFileOperation op) =>
        op with { Path = NormalizePath(op.Path) };

    private PatchNormalizationResult NormalizePatchOperations(
        IReadOnlyList<PatchFileOperation> files,
        IReadOnlyList<PatchEditOperation> edits)
    {
        if (edits.Count == 0)
            return new PatchNormalizationResult(files.Select(NormalizeOperation).ToArray(), []);

        var compiler = new PatchEditCompiler(_rootPath, _roslynNavigation);
        return new PatchNormalizationResult(compiler.Compile(files, edits).ToArray(), compiler.Warnings);
    }

    private IReadOnlyList<string> CreatedPathsThatAreAbsentBeforeApply(IReadOnlyList<PatchFileOperation> operations) =>
        operations
            .Where(op => op.Operation == PatchFileOperationKind.Create)
            .Select(op => op.Path)
            .Where(path =>
            {
                var abs = Path.GetFullPath(Path.Combine(_rootPath, path.Replace('/', Path.DirectorySeparatorChar)));
                return IsUnderRoot(abs, _rootPath) && !File.Exists(abs);
            })
            .ToArray();

    private void ValidateBuildPolicy(PatchPolicy? policy)
    {
        var value = policy?.Policy ?? "none";
        if (string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
            return;

        if (!string.Equals(value, "solution", StringComparison.OrdinalIgnoreCase))
            throw new PatchValidationException(
                "unsupported_patch_policy",
                $"build.policy '{value}' is not supported in this pass; use 'none' or 'solution'.");

        ResolveBuildPath(policy?.Path);
    }

    private void ValidateTestsPolicy(PatchPolicy? policy)
    {
        var value = policy?.Policy ?? "none";
        if (string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
            return;

        if (string.Equals(value, "all", StringComparison.OrdinalIgnoreCase))
        {
            ResolveTestSolutionPath(policy?.Path);
            return;
        }

        if (string.Equals(value, "projects", StringComparison.OrdinalIgnoreCase))
        {
            ValidateTestProjects(policy);
            return;
        }

        if (string.Equals(value, "filter", StringComparison.OrdinalIgnoreCase))
        {
            ValidateTestProjects(policy);
            if (string.IsNullOrWhiteSpace(policy?.Filter))
            {
                throw new PatchValidationException(
                    "invalid_patch_policy",
                    "tests.policy 'filter' requires a non-empty filter.");
            }

            return;
        }

        throw new PatchValidationException(
            "unsupported_patch_policy",
            $"tests.policy '{value}' is not supported; use none, all, projects, or filter.");
    }

    private void ValidateTestProjects(PatchPolicy? policy)
    {
        if (policy?.Projects.Count is not > 0)
        {
            throw new PatchValidationException(
                "invalid_patch_policy",
                $"tests.policy '{policy?.Policy}' requires at least one project path.");
        }

        foreach (var project in policy.Projects)
            ResolveTestPath(project);
    }

    private static IReadOnlyList<ActivePatchFile> MergeFiles(
        IReadOnlyList<ActivePatchFile> current,
        IReadOnlyList<PatchFileOperation> amendments,
        int revision,
        IReadOnlyList<GitStatusFile> changedFiles)
    {
        var byPath = current.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var op in amendments)
        {
            byPath.TryGetValue(op.Path, out var existing);
            byPath[op.Path] = new ActivePatchFile(
                Path: op.Path,
                Operation: DetermineMergedOperation(existing?.Operation, op.Operation),
                OldContentHash: existing is not null ? existing.OldContentHash : op.OldContentHash,
                LastRevision: revision);
        }

        var changed = changedFiles.Select(f => NormalizePath(f.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return byPath.Values
            .Where(f => changed.Contains(f.Path))
            .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string DetermineMergedOperation(string? existingOperation, PatchFileOperationKind amendmentOperation)
    {
        if (existingOperation == "create")
            return amendmentOperation == PatchFileOperationKind.Delete ? "delete" : "create";

        if (existingOperation == "delete" && amendmentOperation is PatchFileOperationKind.Create or PatchFileOperationKind.Replace)
            return "replace";

        return amendmentOperation.ToString().ToLowerInvariant();
    }

    private static PatchFileOperation ToPatchFileOperation(ActivePatchFile file) => new()
    {
        Path = file.Path,
        Operation = file.Operation switch
        {
            "create" => PatchFileOperationKind.Create,
            "delete" => PatchFileOperationKind.Delete,
            _ => PatchFileOperationKind.Replace,
        },
        OldContentHash = file.OldContentHash,
    };

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    private void RecoverFromStore()
    {
        var metadata = _store?.Load();
        if (metadata is null)
            return;

        if (!string.Equals(metadata.RootName, _rootName, StringComparison.OrdinalIgnoreCase))
        {
            _foreignActive = metadata;
            return;
        }

        var status = _gitStatus.GetStatus();
        if (!status.IsRepository || status.IsClean)
        {
            _store?.Clear();
            return;
        }

        // Prefer the persisted file set: it preserves each file's original operation and
        // content-hash anchors, so a recovered patch can be amended with its concurrency guards
        // intact. Fall back to inferring from the dirty tree only for legacy metadata that
        // predates persisted files (hashes are unavailable in that case).
        var files = metadata.Files.Count > 0
            ? metadata.Files.Select(f => new ActivePatchFile(
                Path: NormalizePath(f.Path),
                Operation: f.Operation,
                OldContentHash: f.OldContentHash,
                LastRevision: f.LastRevision == 0 ? metadata.Revision : f.LastRevision)).ToArray()
            : status.ChangedFiles.Select(f => new ActivePatchFile(
                Path: NormalizePath(f.Path),
                Operation: InferOperation(f.Status),
                OldContentHash: null,
                LastRevision: metadata.Revision)).ToArray();

        _active = new ActivePatch(
            PatchId: metadata.PatchId,
            Status: metadata.Status,
            Revision: metadata.Revision,
            Title: metadata.Title,
            Description: metadata.Description,
            CommitMessage: metadata.CommitMessage,
            BaseHeadSha: metadata.BaseHeadSha,
            LastFailureStage: metadata.LastFailureStage,
            Recovered: true,
            BuildPolicy: metadata.BuildPolicy,
            TestPolicy: metadata.TestPolicy,
            Files: files,
            LastBuild: metadata.LastBuild,
            LastTests: metadata.LastTests);
    }

    // The session store is a single app-wide slot shared across roots, and _foreignActive is only
    // captured at construction. Re-read the store before starting or acting on work so a patch
    // begun on another root *after* this service was built is seen - otherwise this root would
    // overwrite that patch's metadata. When we own the active patch there is nothing foreign.
    private void SyncForeignActiveFromStore()
    {
        if (_active is not null || _store is null)
            return;

        var metadata = _store.Load();
        _foreignActive = metadata is not null &&
                         !string.Equals(metadata.RootName, _rootName, StringComparison.OrdinalIgnoreCase)
            ? metadata
            : null;
    }

    private static string InferOperation(string status) =>
        status switch
        {
            "staged_new" or "untracked" => "create",
            "staged_deleted" or "deleted_unstaged" => "delete",
            _ => "replace",
        };

    private static PatchTransactionResult MetadataOnlyResult(PatchSessionMetadata metadata, bool recovered) => new()
    {
        PatchStatus = metadata.Status,
        PatchId = metadata.PatchId,
        Revision = metadata.Revision,
        Title = metadata.Title,
        Description = metadata.Description,
        CommitMessage = metadata.CommitMessage,
        Recovered = recovered,
        LastFailureStage = metadata.LastFailureStage,
        Applied = true,
        DiffVerified = false,
        Build = metadata.LastBuild ?? new PatchStageResult { Status = "skipped", Policy = metadata.BuildPolicy.Policy },
        Tests = metadata.LastTests ?? new PatchStageResult { Status = "skipped", Policy = metadata.TestPolicy.Policy },
    };

    private PatchStageResult RunBuild(PatchPolicy policy)
    {
        if (string.Equals(policy.Policy, "none", StringComparison.OrdinalIgnoreCase))
            return SkippedStage(policy);

        try
        {
            ValidateBuildPolicy(policy);
        }
        catch (PatchValidationException ex)
        {
            return PolicyFailureStage(policy, ex);
        }

        var result = _buildRunner.Run(new BuildRequest
        {
            Path = policy.Path,
            Configuration = policy.Configuration,
            TimeoutSeconds = policy.TimeoutSeconds,
            TreatWarningsAsErrors = policy.TreatWarningsAsErrors,
        });

        return new PatchStageResult
        {
            Status = result.Status,
            Policy = "solution",
            Path = result.Path,
            Configuration = result.Configuration,
            DurationMs = result.DurationMs,
            ExitCode = result.ExitCode,
            Stdout = result.Stdout,
            StdoutTruncated = result.StdoutTruncated,
            Stderr = result.Stderr,
            StderrTruncated = result.StderrTruncated,
            Diagnostics = result.Diagnostics,
        };
    }

    private PatchStageResult RunTests(PatchPolicy policy)
    {
        if (string.Equals(policy.Policy, "none", StringComparison.OrdinalIgnoreCase))
            return SkippedStage(policy);

        try
        {
            ValidateTestsPolicy(policy);
        }
        catch (PatchValidationException ex)
        {
            return PolicyFailureStage(policy, ex);
        }

        var result = _testRunner.Run(new TestRequest
        {
            Policy = policy.Policy,
            Path = policy.Path,
            Projects = policy.Projects,
            Filter = policy.Filter,
            Configuration = policy.Configuration,
            TimeoutSeconds = policy.TimeoutSeconds,
        });

        return new PatchStageResult
        {
            Status = result.Status,
            Policy = policy.Policy,
            Path = result.Path,
            Projects = result.Projects,
            Filter = result.Filter,
            Configuration = result.Configuration,
            DurationMs = result.DurationMs,
            ExitCode = result.ExitCode,
            TotalTests = result.TotalTests,
            ExecutedTests = result.ExecutedTests,
            PassedTests = result.PassedTests,
            FailedTests = result.FailedTests,
            SkippedTests = result.SkippedTests,
            Stdout = result.Stdout,
            StdoutTruncated = result.StdoutTruncated,
            Stderr = result.Stderr,
            StderrTruncated = result.StderrTruncated,
            Diagnostics = result.Diagnostics,
        };
    }

    private static PatchStageResult SkippedStage(PatchPolicy policy) => new()
    {
        Status = "skipped",
        Policy = policy.Policy,
        Path = policy.Path,
        Projects = policy.Projects,
        Filter = policy.Filter,
        Configuration = policy.Configuration,
    };

    private static PatchStageResult ValidatedStage(PatchPolicy policy)
    {
        if (string.Equals(policy.Policy, "none", StringComparison.OrdinalIgnoreCase))
            return SkippedStage(policy);

        return new PatchStageResult
        {
            Status = "validated",
            Policy = policy.Policy,
            Path = policy.Path,
            Projects = policy.Projects,
            Filter = policy.Filter,
            Configuration = policy.Configuration,
        };
    }

    private static PatchStageResult PolicyFailureStage(PatchPolicy policy, PatchValidationException exception) => new()
    {
        Status = "failed",
        Policy = policy.Policy,
        Path = policy.Path,
        Projects = policy.Projects,
        Filter = policy.Filter,
        Configuration = policy.Configuration,
        Diagnostics =
        [
            new BuildDiagnostic
            {
                Kind = "policy",
                Code = exception.Code,
                Message = exception.Message,
            },
        ],
    };

    private PatchSessionMetadata ToMetadata(ActivePatch patch, PatchPolicy build, PatchPolicy tests)
    {
        var now = DateTime.UtcNow;
        return new PatchSessionMetadata
        {
            PatchId = patch.PatchId,
            RootName = _rootName,
            Status = patch.Status,
            Revision = patch.Revision,
            Title = patch.Title,
            Description = patch.Description,
            CommitMessage = patch.CommitMessage,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            BaseHeadSha = patch.BaseHeadSha,
            LastFailureStage = patch.LastFailureStage,
            BuildPolicy = build,
            TestPolicy = tests,
            LastBuild = patch.LastBuild,
            LastTests = patch.LastTests,
            Files = patch.Files.Select(f => new PatchSessionFile
            {
                Path = f.Path,
                Operation = f.Operation,
                OldContentHash = f.OldContentHash,
                LastRevision = f.LastRevision,
            }).ToArray(),
        };
    }

    private string ResolveBuildPath(string? requestedPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedPath))
        {
            var candidate = Path.GetFullPath(Path.Combine(_rootPath, requestedPath));
            if (!IsUnderRoot(candidate, _rootPath))
                throw new PatchValidationException("path_outside_sandbox", $"Build path is outside the root: {requestedPath}");
            if (!File.Exists(candidate))
                throw new PatchValidationException("file_not_found", $"Build path not found: {requestedPath}");
            return candidate;
        }

        var solution = Directory.EnumerateFiles(_rootPath, "*.slnx", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(_rootPath, "*.sln", SearchOption.TopDirectoryOnly))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (solution is null)
            throw new PatchValidationException("file_not_found", "No .slnx or .sln file found in the active root.");

        return solution;
    }

    private string ResolveTestSolutionPath(string? requestedPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedPath))
            return ResolveTestPath(requestedPath);

        var solution = Directory.EnumerateFiles(_rootPath, "*.slnx", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(_rootPath, "*.sln", SearchOption.TopDirectoryOnly))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (solution is null)
            throw new PatchValidationException("file_not_found", "No .slnx or .sln file found in the active root.");

        return solution;
    }

    private string ResolveTestPath(string requestedPath)
    {
        if (string.IsNullOrWhiteSpace(requestedPath))
            throw new PatchValidationException("invalid_patch_policy", "Test project path is required.");

        var candidate = Path.GetFullPath(Path.Combine(_rootPath, requestedPath));
        if (!IsUnderRoot(candidate, _rootPath))
            throw new PatchValidationException("path_outside_sandbox", $"Test path is outside the root: {requestedPath}");
        if (!File.Exists(candidate))
            throw new PatchValidationException("file_not_found", $"Test path not found: {requestedPath}");
        return candidate;
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

    private sealed record ActivePatch(
        string PatchId,
        string Status,
        int Revision,
        string? Title,
        string? Description,
        string? CommitMessage,
        string BaseHeadSha,
        string? LastFailureStage,
        bool Recovered,
        PatchPolicy BuildPolicy,
        PatchPolicy TestPolicy,
        IReadOnlyList<ActivePatchFile> Files,
        // Build/test results from the run that validated this patch, retained so a later Accept
        // (or Current) reports the real outcomes instead of "skipped".
        PatchStageResult? LastBuild = null,
        PatchStageResult? LastTests = null);

    private sealed record ActivePatchFile(
        string Path,
        string Operation,
        string? OldContentHash,
        int LastRevision);

    private sealed record PatchNormalizationResult(
        IReadOnlyList<PatchFileOperation> Operations,
        IReadOnlyList<PatchWarning> Warnings);

    private sealed record FileSnapshot(string Path, byte[]? Content);
}
