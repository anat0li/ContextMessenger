using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContextMessenger.App.Wpf.Logging;
using ContextMessenger.App.Wpf.Patching;
using ContextMessenger.App.Wpf.Services;
using ContextMessenger.App.Wpf.Settings;
using ContextMessenger.Core.Meta;
using ContextMessenger.Core.Patching;
using ContextMessenger.Patching;
using ContextMessenger.Protocol;
using Microsoft.Extensions.Logging;
using System.Windows;
using System.Windows.Threading;

namespace ContextMessenger.App.Wpf.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IFolderPicker _folderPicker;
    private readonly ISettingsStore _settings;
    private readonly IProfilesEditor _profilesEditor;
    private readonly IClipboardService _clipboard;
    private readonly ISqlRootSettingsDialogService _sqlRootSettingsDialog;
    private readonly LoopLogStore _logStore;
    private readonly PatchReviewService _reviewService;
    private readonly LoopManager _loopManager;
    private readonly AppRootSwitchCoordinator? _rootSwitchCoordinator;
    private readonly SynchronizationContext? _uiContext;
    private readonly Dispatcher? _uiDispatcher;
    // The loop that owns the patch currently on the review tab, so closing that tab returns focus
    // to the same root's log rather than whatever log tab happened to be selected.
    private ProcessingLoopViewModel? _reviewOwnerLoop;
    private int _largePayloadThresholdBytes = 32_768;
    private LoggingSettings _logging = new();
    private bool _isLoading;
    private bool _isSynchronizingSelection;
    private bool _isChangingAutoProcess;

    [ObservableProperty]
    private ObservableCollection<TargetProfile> _targets = new();

    [ObservableProperty]
    private ObservableCollection<RootProfile> _roots = new();

    [ObservableProperty]
    private ObservableCollection<ProcessingLoopViewModel> _loops = new();

    /// <summary>
    /// Items shown in the tab strip: every loop log tab, plus the single
    /// <see cref="PatchReview"/> tab while a patch is under review. Bound to the TabControl so
    /// the review page opens and closes as a real tab.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<object> _tabs = new();

    /// <summary>The currently selected tab item (a <see cref="ProcessingLoopViewModel"/> or the review VM).</summary>
    [ObservableProperty]
    private object? _selectedTab;

    [ObservableProperty]
    private TargetProfile? _selectedTarget;

    [ObservableProperty]
    private RootProfile? _selectedRoot;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearLogCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleReviewCommand))]
    private ProcessingLoopViewModel? _selectedLoop;

    [ObservableProperty]
    private string _status = "Ready";

    /// <summary>Long descriptive hint for the toolbar button under the mouse, shown at the right of the status bar.</summary>
    [ObservableProperty]
    private string _statusHint = "";

    /// <summary>
    /// Review state for the active held patch. Populated from live patch outcomes by the
    /// loop gating; its actions are routed to the loop that owns the held patch.
    /// </summary>
    public PatchReviewViewModel PatchReview => _reviewService.PatchReview;

    public MainViewModel(
        IFolderPicker folderPicker,
        ISettingsStore settings,
        IProfilesEditor profilesEditor,
        IClipboardService clipboard,
        ISqlRootSettingsDialogService sqlRootSettingsDialog,
        LoopLogStore logStore,
        PatchReviewService reviewService,
        LoopManager loopManager,
        AppRootSwitchCoordinator? rootSwitchCoordinator = null)
    {
        _folderPicker = folderPicker ?? throw new ArgumentNullException(nameof(folderPicker));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _profilesEditor = profilesEditor ?? throw new ArgumentNullException(nameof(profilesEditor));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _sqlRootSettingsDialog = sqlRootSettingsDialog ?? throw new ArgumentNullException(nameof(sqlRootSettingsDialog));
        _logStore = logStore ?? throw new ArgumentNullException(nameof(logStore));
        _reviewService = reviewService ?? throw new ArgumentNullException(nameof(reviewService));
        _loopManager = loopManager ?? throw new ArgumentNullException(nameof(loopManager));
        _rootSwitchCoordinator = rootSwitchCoordinator;
        _uiContext = SynchronizationContext.Current;
        _uiDispatcher = Application.Current?.Dispatcher;

        _loopManager.LogProduced += AppendLog;
        _loopManager.StatusChanged += ApplyRuntimeStatus;
        _loopManager.PatchInteractionChanged += OnPatchInteractionChanged;

        PatchReview.PropertyChanged += OnPatchReviewPropertyChanged;

        var loaded = SafeLoadSettings();
        _largePayloadThresholdBytes = loaded.LargePayloadThresholdBytes;
        _logging = loaded.Logging;

        _isLoading = true;
        Targets = new ObservableCollection<TargetProfile>(loaded.Targets);
        Roots = new ObservableCollection<RootProfile>(loaded.Roots);
        SelectedTarget = FindByName(Targets, loaded.CurrentTargetName) ?? Targets.FirstOrDefault();
        SelectedRoot = FindByName(Roots, ResolveInitialRootName(loaded, SelectedTarget)) ?? Roots.FirstOrDefault();
        _isLoading = false;

        CreateConfiguredLoops(loaded);
        SelectInitialLoop(loaded);
        RestoreAutoProcessLoops(loaded);
        RestoreHeldReview();

        if (_rootSwitchCoordinator is not null)
            _rootSwitchCoordinator.RootSwitchRequested += OnRootSwitchRequested;
    }

    [RelayCommand]
    private void ToggleAutoProcess()
    {
        if (SelectedLoop is null) return;
        SelectedLoop.IsAutoProcessEnabled = !SelectedLoop.IsAutoProcessEnabled;
    }

    [RelayCommand(CanExecute = nameof(CanToggleReview))]
    private void ToggleReview()
    {
        var loop = SelectedLoop;
        if (loop is null) return;

        var enabled = !loop.IsPatchReviewEnabled;
        loop.IsPatchReviewEnabled = enabled;
        if (_loopManager.TryGetContext(loop, out var context))
            context.Patches.DeferAcceptanceByDefault = enabled;
        UpdateRootReviewSetting(loop.Root.Name, enabled);
        PersistSettings();
        Status = enabled
            ? $"Manual patch review enabled for {loop.Root.Name}."
            : $"Automatic patch processing enabled for {loop.Root.Name}.";
    }

    // Enabled only when a loop is active and no patch is currently under review.
    private bool CanToggleReview() =>
        SelectedLoop?.SupportsPatchReview == true && !PatchReview.HasInteraction;

    private void UpdateRootReviewSetting(string rootName, bool enabled)
    {
        for (var i = 0; i < Roots.Count; i++)
        {
            if (string.Equals(Roots[i].Name, rootName, StringComparison.Ordinal))
            {
                Roots[i] = Roots[i] with { HoldPatchResponsesForReview = enabled };
                return;
            }
        }
    }

    // A loop processed a patch outcome (raised on a background thread): open, refresh, or
    // close the review page, and route the actions to the owning loop.
    private void OnPatchInteractionChanged(ProcessingLoopViewModel loop)
    {
        void Apply()
        {
            if (_reviewService.Store.Current is not null && _loopManager.TryGetContext(loop, out _))
                _reviewOwnerLoop = loop;

            _reviewService.Project(_loopManager.TryGetContext(loop, out var projected) ? projected.Actions : null);
        }

        if (_uiContext is null || SynchronizationContext.Current == _uiContext)
            Apply();
        else
            _uiContext.Post(_ => Apply(), null);
    }

    private void OnPatchReviewPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PatchReviewViewModel.HasInteraction))
        {
            UpdateReviewTabPresence();
            ToggleReviewCommand.NotifyCanExecuteChanged();
        }
    }

    private void RestoreHeldReview()
    {
        try
        {
            var owner = _reviewService.Restore();
            if (owner is null)
                return;

            var target = FindByName(Targets, owner.TargetName);
            var root = FindByName(Roots, owner.RootName);
            if (target is null || root is null)
            {
                ClearRestoredReview();
                return;
            }

            var loop = GetOrCreateLoop(target, root);
            var runtime = _loopManager.GetOrCreate(loop);
            runtime.MarkRequestIdSeen(_reviewService.Store.Current?.RequestId ?? "");
            _loopManager.TryGetContext(loop, out var context);
            _reviewOwnerLoop = loop;
            // Do not auto-refresh here: RefreshAsync closes the review when the patch service
            // reports no active patch, which can happen during recovery edge cases. Re-open the
            // durable review first; explicit Refresh/Revert/Accept can reconcile terminal state.
            _reviewService.Project(context.Actions);
            UpdateReviewTabPresence();
        }
        catch (Exception ex)
        {
            ClearRestoredReview();
            Status = $"Could not restore held review: {ex.Message}";
        }
    }

    private void ClearRestoredReview()
    {
        _reviewService.Store.Clear();
        _reviewService.RefreshProjection();
    }

    [RelayCommand(CanExecute = nameof(CanClearLog))]
    private void ClearLog()
    {
        var loop = SelectedLoop;
        if (loop is null) return;

        try
        {
            _logStore.Clear(loop.Target.Name, loop.Root.Name);
            loop.ClearLog();
            Status = $"Cleared log for {loop.Title}.";
        }
        catch (Exception ex)
        {
            Status = $"Could not clear log: {ex.Message}";
        }
    }

    private bool CanClearLog() => SelectedLoop is not null;

    [RelayCommand]
    private void BrowseRoot()
    {
        var picked = _folderPicker.PickFolder(SelectedRoot?.Path);
        if (picked is null) return;

        var existing = Roots.FirstOrDefault(r =>
            string.Equals(r.Path, picked, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            existing = new RootProfile
            {
                Name = MakeRootName(picked),
                Path = picked,
            };
            Roots.Add(existing);
        }

        SelectedRoot = existing;
        PersistSettings();
    }

    [RelayCommand]
    private void ManageProfiles()
    {
        try
        {
            _profilesEditor.OpenForEdit();
            Status = "Opened appsettings.json - restart to apply manual edits.";
        }
        catch (Exception ex)
        {
            Status = $"Could not open appsettings.json: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CopyProtocolPrompt()
    {
        try
        {
            _clipboard.SetText(SystemPromptProvider.Generate());
            Status = "Copied protocol prompt to clipboard.";
        }
        catch (Exception ex)
        {
            Status = $"Could not copy protocol prompt: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ManageSqlRoots()
    {
        var result = _sqlRootSettingsDialog.Show(Roots, SelectedRoot);
        if (result is null)
            return;

        await ApplySqlRootDialogResult(result);
    }

    private async Task ApplySqlRootDialogResult(SqlRootDialogResult result)
    {
        var root = result.Root;
        var existingIndex = FindRootIndex(root.Name);
        var existing = existingIndex >= 0 ? Roots[existingIndex] : null;

        if (result.IsNewRoot)
        {
            if (existing is not null)
            {
                Status = $"SQL root '{root.Name}' already exists.";
                return;
            }

            Roots.Add(root);
        }
        else if (existingIndex >= 0 && !Equals(existing, root))
        {
            Roots[existingIndex] = root;
        }
        else
        {
            Roots.Add(root);
        }

        if (SelectedTarget is not null)
            await RemoveStaleLoopForRoot(SelectedTarget, root);

        _isSynchronizingSelection = true;
        try
        {
            SelectedRoot = root;
            if (SelectedTarget is not null)
            {
                var loop = GetOrCreateLoop(SelectedTarget, root);
                SelectedLoop = loop;
                SelectedTab = loop;
            }
        }
        finally
        {
            _isSynchronizingSelection = false;
        }

        PersistSettings();
        Status = result.IsNewRoot
            ? $"Added and selected SQL root {root.Name}."
            : $"Selected SQL root {root.Name}.";
    }

    partial void OnSelectedRootChanged(RootProfile? value)
    {
        if (_isLoading) return;

        SelectLoopFromCombos(disablePreviousRootForTarget: true);
        PersistSettings();
    }

    partial void OnSelectedTargetChanged(TargetProfile? value)
    {
        if (_isLoading) return;

        SelectLoopFromCombos(disablePreviousRootForTarget: false);
        PersistSettings();
    }

    partial void OnSelectedLoopChanged(ProcessingLoopViewModel? value)
    {
        // Keep the tab strip pointed at the active loop, unless the review tab is currently up
        // (the review tab stays selected while a patch is held, regardless of loop selection).
        if (value is not null && !ReferenceEquals(SelectedTab, value) && !ReferenceEquals(SelectedTab, PatchReview))
            SelectedTab = value;

        if (_isLoading || _isSynchronizingSelection || value is null) return;

        _isSynchronizingSelection = true;
        try
        {
            SelectedTarget = value.Target;
            SelectedRoot = value.Root;
        }
        finally
        {
            _isSynchronizingSelection = false;
        }

        DisableOtherRunningRootsForTarget(value);
        PersistSettings();
    }

    partial void OnSelectedTabChanged(object? value)
    {
        // The patch actions are live only while the review tab is the current tab; selecting a
        // log tab disables them. This is what keeps the patch buttons off on log pages.
        PatchReview.IsActive = ReferenceEquals(value, PatchReview);

        // Selecting a log tab makes that loop the active loop (drives combos and commands).
        if (value is ProcessingLoopViewModel loop)
            SelectedLoop = loop;
    }

    // Adds or removes the single review tab to mirror PatchReview.HasInteraction, activating it
    // when a patch becomes held so the reviewer lands on the page. Runs on the UI thread.
    private void UpdateReviewTabPresence()
    {
        var present = Tabs.Contains(PatchReview);
        if (PatchReview.HasInteraction && !present)
        {
            Tabs.Add(PatchReview);
            SelectedTab = PatchReview;
        }
        else if (!PatchReview.HasInteraction && present)
        {
            // Move selection to the patch's own root log first, then drop the review tab, so the
            // TabControl never briefly auto-selects an unrelated tab.
            if (ReferenceEquals(SelectedTab, PatchReview))
            {
                var owner = _reviewOwnerLoop is not null && Tabs.Contains(_reviewOwnerLoop)
                    ? _reviewOwnerLoop
                    : SelectedLoop ?? Loops.FirstOrDefault();
                SelectedTab = owner;
            }

            Tabs.Remove(PatchReview);
            _reviewOwnerLoop = null;
        }
    }

    private void InsertLoopTab(ProcessingLoopViewModel loop)
    {
        // Keep the review tab (when present) pinned last in the strip.
        if (Tabs.Contains(PatchReview))
            Tabs.Insert(Tabs.Count - 1, loop);
        else
            Tabs.Add(loop);
    }

    private void SelectLoopFromCombos(bool disablePreviousRootForTarget)
    {
        if (_isSynchronizingSelection) return;
        if (SelectedTarget is null || SelectedRoot is null) return;

        var previous = SelectedLoop;
        var next = GetOrCreateLoop(SelectedTarget, SelectedRoot);

        _isSynchronizingSelection = true;
        try
        {
            SelectedLoop = next;
        }
        finally
        {
            _isSynchronizingSelection = false;
        }

        if (disablePreviousRootForTarget)
            DisablePreviousRootForTarget(previous, next);
    }

    private ProcessingLoopViewModel GetOrCreateLoop(TargetProfile target, RootProfile root)
    {
        var loop = Loops.FirstOrDefault(l => IsSameTarget(l.Target, target) && IsSameRoot(l.Root, root));
        if (loop is not null)
            return loop;

        loop = new ProcessingLoopViewModel(target, root, _logging);
        loop.LoadLogLines(_logStore.Load(target.Name, root.Name));

        loop.PropertyChanged += OnLoopPropertyChanged;
        Loops.Add(loop);
        InsertLoopTab(loop);
        CloseLoopCommand.NotifyCanExecuteChanged();
        return loop;
    }

    private async Task RemoveStaleLoopForRoot(TargetProfile target, RootProfile root)
    {
        var loop = Loops.FirstOrDefault(l => IsSameTarget(l.Target, target) && IsSameRoot(l.Root, root));
        if (loop is null || Equals(loop.Root, root))
            return;

        if (loop.IsRunning)
            await StopLoopAsync(loop);

        loop.PropertyChanged -= OnLoopPropertyChanged;
        var closedContext = _loopManager.Remove(loop);
        if (closedContext is not null &&
            ReferenceEquals(_reviewService.Router.Target, closedContext.Actions))
        {
            _reviewService.Router.Target = null;
        }

        Loops.Remove(loop);
        Tabs.Remove(loop);
        CloseLoopCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanCloseLoop))]
    private async Task CloseLoop(ProcessingLoopViewModel? loop)
    {
        if (loop is null || Loops.Count <= 1)
            return;

        var index = Loops.IndexOf(loop);
        if (index < 0)
            return;

        if (loop.IsAutoProcessEnabled)
            loop.IsAutoProcessEnabled = false;
        else
            await StopLoopAsync(loop);

        loop.PropertyChanged -= OnLoopPropertyChanged;
        var closedContext = _loopManager.Remove(loop);
        if (closedContext is not null &&
            ReferenceEquals(_reviewService.Router.Target, closedContext.Actions))
        {
            _reviewService.Router.Target = null;
        }

        Loops.RemoveAt(index);
        Tabs.Remove(loop);
        SelectedLoop = Loops[Math.Min(index, Loops.Count - 1)];
        CloseLoopCommand.NotifyCanExecuteChanged();
        PersistSettings();
    }

    private bool CanCloseLoop(ProcessingLoopViewModel? loop) => loop is not null && Loops.Count > 1;

    private async void OnLoopPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isChangingAutoProcess || e.PropertyName != nameof(ProcessingLoopViewModel.IsAutoProcessEnabled))
            return;
        if (sender is not ProcessingLoopViewModel loop)
            return;

        if (loop.IsAutoProcessEnabled)
        {
            foreach (var other in Loops.Where(l => !ReferenceEquals(l, loop) && IsSameTarget(l.Target, loop.Target) && l.IsAutoProcessEnabled))
                other.IsAutoProcessEnabled = false;

            await StartLoopAsync(loop);
        }
        else
        {
            await StopLoopAsync(loop);
        }

        PersistSettings();
    }

    private async Task StartLoopAsync(ProcessingLoopViewModel loop)
    {
        var runtime = await _loopManager.StartAsync(loop);
        ApplyRuntimeStatus(loop, runtime);

        if (!runtime.IsRunning)
            SetAutoProcessWithoutHandling(loop, false);
    }

    private async Task StopLoopAsync(ProcessingLoopViewModel loop)
    {
        var runtime = await _loopManager.StopAsync(loop);
        if (runtime is not null)
        {
            ApplyRuntimeStatus(loop, runtime);
        }
        else
        {
            loop.IsRunning = false;
            loop.Status = "Idle";
            Status = SelectedLoop is null ? "Ready" : SelectedLoop.Status;
        }
    }

    private void ApplyRuntimeStatus(ProcessingLoopViewModel loop, IMessageProcessingLoop runtime)
    {
        void Apply()
        {
            loop.IsRunning = runtime.IsRunning;
            loop.Status = runtime.Status switch
            {
                MessageProcessingLoopStatus.Idle when runtime.IsRunning => $"Watching {loop.Target.Name} for {loop.Root.Name}",
                MessageProcessingLoopStatus.Processing => "Processing request...",
                MessageProcessingLoopStatus.Error => "Error",
                _ => "Idle",
            };

            if (ReferenceEquals(SelectedLoop, loop))
                Status = loop.Status;
        }

        if (_uiContext is null || SynchronizationContext.Current == _uiContext)
        {
            Apply();
            return;
        }

        _uiContext.Post(_ => Apply(), null);
    }

    private void DisablePreviousRootForTarget(ProcessingLoopViewModel? previous, ProcessingLoopViewModel next)
    {
        if (previous is null || ReferenceEquals(previous, next)) return;
        if (!IsSameTarget(previous.Target, next.Target)) return;
        if (IsSameRoot(previous.Root, next.Root)) return;
        if (!previous.IsAutoProcessEnabled) return;

        previous.IsAutoProcessEnabled = false;
    }

    private void DisableOtherRunningRootsForTarget(ProcessingLoopViewModel loop)
    {
        foreach (var other in Loops.Where(l => !ReferenceEquals(l, loop) && IsSameTarget(l.Target, loop.Target) && l.IsAutoProcessEnabled))
            other.IsAutoProcessEnabled = false;
    }

    private void SetAutoProcessWithoutHandling(ProcessingLoopViewModel loop, bool value)
    {
        _isChangingAutoProcess = true;
        try
        {
            loop.IsAutoProcessEnabled = value;
        }
        finally
        {
            _isChangingAutoProcess = false;
        }
    }

    private void AppendError(ProcessingLoopViewModel loop, string message, Exception ex)
    {
        AppendLog(loop, new LogEntry
        {
            Timestamp = DateTimeOffset.Now,
            Level = LogLevel.Error,
            Kind = LogEntryKind.Error,
            Message = $"{message}{Environment.NewLine}{ex}",
        });
    }

    private void AppendLog(ProcessingLoopViewModel loop, LogEntry entry)
    {
        void Append()
        {
            if (_logging.EnableUiLogging)
                loop.Append(entry);
            _logStore.Append(loop.Target.Name, loop.Root.Name, entry);
        }

        if (_uiContext is null || SynchronizationContext.Current == _uiContext)
        {
            Append();
            return;
        }

        _uiContext.Post(_ => Append(), null);
    }

    private AppSettings SafeLoadSettings()
    {
        try { return _settings.Load(); }
        catch { return new AppSettings(); }
    }

    private void PersistSettings()
    {
        TrySaveSettings(new AppSettings
        {
            Targets = BuildTargetSettings(),
            Roots = Roots.ToArray(),
            CurrentTargetName = SelectedTarget?.Name,
            LargePayloadThresholdBytes = _largePayloadThresholdBytes,
            Logging = _logging,
        });
    }

    private void TrySaveSettings(AppSettings settings)
    {
        try { _settings.Save(settings); }
        catch (Exception ex) { Status = $"Could not save settings: {ex.Message}"; }
    }

    private static bool IsSameTarget(TargetProfile left, TargetProfile right) =>
        string.Equals(left.Name, right.Name, StringComparison.Ordinal);

    private static bool IsSameRoot(RootProfile left, RootProfile right) =>
        string.Equals(left.Name, right.Name, StringComparison.Ordinal);

    private int FindRootIndex(string rootName)
    {
        for (var i = 0; i < Roots.Count; i++)
        {
            if (string.Equals(Roots[i].Name, rootName, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private static T? FindByName<T>(IEnumerable<T> items, string? name)
        where T : class
    {
        if (string.IsNullOrEmpty(name)) return null;

        return items.FirstOrDefault(item => item switch
        {
            TargetProfile target => string.Equals(target.Name, name, StringComparison.Ordinal),
            RootProfile root => string.Equals(root.Name, name, StringComparison.Ordinal),
            _ => false,
        });
    }

    private static string MakeRootName(string path)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }

    private void CreateConfiguredLoops(AppSettings settings)
    {
        foreach (var target in Targets)
        {
            var configuredTarget = settings.Targets.FirstOrDefault(t => IsSameTarget(t, target));
            if (configuredTarget is null)
                continue;

            foreach (var rootSetting in OrderedRootSettings(configuredTarget))
            {
                var root = FindByName(Roots, rootSetting.RootName);
                if (root is not null)
                    GetOrCreateLoop(target, root);
            }
        }
    }

    private void RestoreAutoProcessLoops(AppSettings settings)
    {
        foreach (var loop in Loops.ToArray())
        {
            var rootSettings = settings.Targets
                .FirstOrDefault(t => IsSameTarget(t, loop.Target))
                ?.Roots;
            if (rootSettings?.FirstOrDefault(r => string.Equals(r.RootName, loop.Root.Name, StringComparison.Ordinal))?.AutoProcessEnabled is true)
                loop.IsAutoProcessEnabled = true;
        }
    }

    private IReadOnlyList<TargetProfile> BuildTargetSettings()
    {
        return Targets.Select(target => target with
        {
            Roots = Loops
                .Where(loop => IsSameTarget(loop.Target, target))
                .Select((loop, index) => new TargetRootSettings
                {
                    RootName = loop.Root.Name,
                    AutoProcessEnabled = loop.IsAutoProcessEnabled,
                    Order = index,
                    IsActive = ReferenceEquals(loop, SelectedLoop),
                })
                .ToArray(),
        }).ToArray();
    }

    private static string? ResolveInitialRootName(AppSettings settings, TargetProfile? target)
    {
        if (!string.IsNullOrEmpty(settings.CurrentRootName))
            return settings.CurrentRootName;
        if (target is null)
            return null;

        var configuredTarget = settings.Targets.FirstOrDefault(t => IsSameTarget(t, target));
        var orderedRoots = configuredTarget is null ? [] : OrderedRootSettings(configuredTarget).ToArray();

        return orderedRoots?.FirstOrDefault(r => r.AutoProcessEnabled)?.RootName ??
               orderedRoots?.FirstOrDefault(r => r.IsActive)?.RootName ??
               orderedRoots?.FirstOrDefault()?.RootName;
    }

    private void SelectInitialLoop(AppSettings settings)
    {
        var autoLoopForSelectedTarget = FindConfiguredAutoLoopForSelectedTarget(settings);
        if (autoLoopForSelectedTarget is not null)
        {
            SelectLoopFromTab(autoLoopForSelectedTarget);
            return;
        }

        var activeLoop = FindConfiguredActiveLoop(settings);
        if (activeLoop is not null)
        {
            SelectLoopFromTab(activeLoop);
            return;
        }

        SelectLoopFromCombos(disablePreviousRootForTarget: false);
    }

    private ProcessingLoopViewModel? FindConfiguredAutoLoopForSelectedTarget(AppSettings settings)
    {
        if (SelectedTarget is null)
            return null;

        var target = settings.Targets.FirstOrDefault(t => IsSameTarget(t, SelectedTarget));
        var rootName = target is null
            ? null
            : OrderedRootSettings(target).FirstOrDefault(r => r.AutoProcessEnabled)?.RootName;
        if (string.IsNullOrEmpty(rootName))
            return null;

        return Loops.FirstOrDefault(loop =>
            IsSameTarget(loop.Target, SelectedTarget) &&
            string.Equals(loop.Root.Name, rootName, StringComparison.Ordinal));
    }

    private ProcessingLoopViewModel? FindConfiguredActiveLoop(AppSettings settings)
    {
        foreach (var target in settings.Targets)
        {
            foreach (var root in OrderedRootSettings(target))
            {
                if (!root.IsActive)
                    continue;

                var loop = Loops.FirstOrDefault(l =>
                    string.Equals(l.Target.Name, target.Name, StringComparison.Ordinal) &&
                    string.Equals(l.Root.Name, root.RootName, StringComparison.Ordinal));
                if (loop is not null)
                    return loop;
            }
        }

        return null;
    }

    private void SelectLoopFromTab(ProcessingLoopViewModel loop)
    {
        _isSynchronizingSelection = true;
        try
        {
            SelectedLoop = loop;
            SelectedTarget = loop.Target;
            SelectedRoot = loop.Root;
        }
        finally
        {
            _isSynchronizingSelection = false;
        }
    }

    private static IEnumerable<TargetRootSettings> OrderedRootSettings(TargetProfile target) =>
        target.Roots
            .Select((root, index) => new { Root = root, Index = index })
            .OrderBy(item => item.Root.Order ?? int.MaxValue)
            .ThenBy(item => item.Index)
            .Select(item => item.Root);

    private void OnRootSwitchRequested(string targetName, string rootName)
    {
        void Apply()
        {
            var target = Targets.FirstOrDefault(t =>
                string.Equals(t.Name, targetName, StringComparison.OrdinalIgnoreCase));
            if (target is null) return;

            var root = Roots.FirstOrDefault(r =>
                string.Equals(r.Name, rootName, StringComparison.OrdinalIgnoreCase));
            if (root is null) return;

            var loop = GetOrCreateLoop(target, root);
            SelectLoopFromTab(loop);
            SelectedTab = loop;
            if (loop.IsAutoProcessEnabled)
                return;

            loop.IsAutoProcessEnabled = true;
        }

        if (_uiDispatcher is not null && !_uiDispatcher.CheckAccess())
        {
            _uiDispatcher.BeginInvoke(Apply);
            return;
        }

        if (_uiContext is null || SynchronizationContext.Current == _uiContext)
        {
            Apply();
            return;
        }

        _uiContext.Post(_ => Apply(), null);
    }

    public void Dispose()
    {
        if (_rootSwitchCoordinator is not null)
            _rootSwitchCoordinator.RootSwitchRequested -= OnRootSwitchRequested;

        _loopManager.Dispose();
    }

}

internal static class LogEntryExtensions
{
    public static LogEntry WithMessage(this LogEntry entry, string message) => new()
    {
        Timestamp = entry.Timestamp,
        Level = entry.Level,
        Kind = entry.Kind,
        Message = message,
        RepeatCount = entry.RepeatCount,
    };
}
