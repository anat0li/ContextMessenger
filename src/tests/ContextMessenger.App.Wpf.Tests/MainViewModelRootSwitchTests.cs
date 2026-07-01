using System.IO;
using ContextMessenger.App.Wpf.Logging;
using ContextMessenger.App.Wpf.Services;
using ContextMessenger.App.Wpf.Settings;
using ContextMessenger.App.Wpf.ViewModels;
using ContextMessenger.Core.Meta;
using ContextMessenger.Protocol;

namespace ContextMessenger.App.Wpf.Tests;

public sealed class MainViewModelRootSwitchTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "ContextMessengerRootSwitch_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Set_root_activation_selects_destination_log_tab()
    {
        var target = new TargetProfile
        {
            Name = "ChatGPT",
            ProcessName = "ChatGPT",
            Roots =
            [
                new TargetRootSettings
                {
                    RootName = "Repository",
                    IsActive = true,
                },
            ],
        };
        var repository = new RootProfile
        {
            Name = "Repository",
            Path = "C:/repo",
        };
        var database = new RootProfile
        {
            Name = "SQLite Test",
            Kind = RootKind.Sql,
            Sql = new SqlRootSettings
            {
                ProviderInvariantName = "Microsoft.Data.Sqlite",
                ConnectionStringRef = "literal:Data Source=test.db",
            },
        };
        var settings = new TestSettingsStore(new AppSettings
        {
            Targets = [target],
            Roots = [repository, database],
            CurrentTargetName = target.Name,
            CurrentRootName = repository.Name,
        });
        var coordinator = new AppRootSwitchCoordinator();
        var review = new PatchReviewService(
            new FileHeldReviewStore(Path.Combine(directory, "review.json")));
        using var manager = new LoopManager(new TestRuntimeFactory());
        using var viewModel = new MainViewModel(
            new TestFolderPicker(),
            settings,
            new TestProfilesEditor(),
            new TestClipboardService(),
            new TestSqlRootSettingsDialog(),
            new LoopLogStore(directory, 524_288, settings.Current),
            review,
            manager,
            coordinator);

        Assert.Equal("Repository", viewModel.SelectedLoop?.Root.Name);

        coordinator.ActivateRootForTarget(target.Name, database.Name);

        var selected = Assert.IsType<ProcessingLoopViewModel>(viewModel.SelectedTab);
        Assert.Equal("SQLite Test", selected.Root.Name);
        Assert.Same(selected, viewModel.SelectedLoop);
        Assert.Same(database, viewModel.SelectedRoot);
        Assert.Same(target, viewModel.SelectedTarget);
        Assert.True(selected.IsAutoProcessEnabled);
    }

    [Fact]
    public void Copy_protocol_prompt_copies_generated_prompt_to_clipboard()
    {
        var target = new TargetProfile { Name = "ChatGPT", ProcessName = "ChatGPT" };
        var root = new RootProfile { Name = "Repository", Path = "C:/repo" };
        var settings = new TestSettingsStore(new AppSettings
        {
            Targets = [target],
            Roots = [root],
            CurrentTargetName = target.Name,
            CurrentRootName = root.Name,
        });
        var clipboard = new TestClipboardService();
        var review = new PatchReviewService(
            new FileHeldReviewStore(Path.Combine(directory, "review.json")));
        using var manager = new LoopManager(new TestRuntimeFactory());
        using var viewModel = new MainViewModel(
            new TestFolderPicker(),
            settings,
            new TestProfilesEditor(),
            clipboard,
            new TestSqlRootSettingsDialog(),
            new LoopLogStore(directory, 524_288, settings.Current),
            review,
            manager);

        viewModel.CopyProtocolPromptCommand.Execute(null);

        Assert.Equal(SystemPromptProvider.Generate(), clipboard.Text);
        Assert.Equal("Copied protocol prompt to clipboard.", viewModel.Status);
    }

    [Fact]
    public async Task Manage_sql_roots_adds_and_selects_new_sql_root()
    {
        var target = new TargetProfile { Name = "ChatGPT", ProcessName = "ChatGPT" };
        var repository = new RootProfile { Name = "Repository", Path = "C:/repo" };
        var database = new RootProfile
        {
            Name = "Database",
            Kind = RootKind.Sql,
            Sql = new SqlRootSettings
            {
                ProviderInvariantName = "Microsoft.Data.Sqlite",
                ConnectionStringRef = "literal:Data Source=test.db",
                ReadOnly = true,
            },
        };
        var settings = new TestSettingsStore(new AppSettings
        {
            Targets = [target],
            Roots = [repository],
            CurrentTargetName = target.Name,
            CurrentRootName = repository.Name,
        });
        var dialog = new TestSqlRootSettingsDialog
        {
            Result = new SqlRootDialogResult(database, IsNewRoot: true),
        };
        var review = new PatchReviewService(
            new FileHeldReviewStore(Path.Combine(directory, "review.json")));
        using var manager = new LoopManager(new TestRuntimeFactory());
        using var viewModel = new MainViewModel(
            new TestFolderPicker(),
            settings,
            new TestProfilesEditor(),
            new TestClipboardService(),
            dialog,
            new LoopLogStore(directory, 524_288, settings.Current),
            review,
            manager);

        await viewModel.ManageSqlRootsCommand.ExecuteAsync(null);

        Assert.Same(database, viewModel.SelectedRoot);
        Assert.Contains(settings.Current.Roots, root => root.Name == "Database");
        Assert.Equal("Added and selected SQL root Database.", viewModel.Status);
    }

    [Fact]
    public async Task Manage_sql_roots_replaces_existing_sql_root_and_selects_updated_instance()
    {
        var target = new TargetProfile { Name = "ChatGPT", ProcessName = "ChatGPT" };
        var root = new RootProfile
        {
            Name = "Database",
            Kind = RootKind.Sql,
            Sql = new SqlRootSettings
            {
                ProviderInvariantName = "Microsoft.Data.Sqlite",
                ConnectionStringRef = "literal:Data Source=old.db",
                ReadOnly = true,
            },
        };
        var updated = root with
        {
            Sql = root.Sql! with
            {
                ConnectionStringRef = "literal:Data Source=new.db",
                MaxRows = 25,
            },
        };
        var settings = new TestSettingsStore(new AppSettings
        {
            Targets = [target],
            Roots = [root],
            CurrentTargetName = target.Name,
            CurrentRootName = root.Name,
        });
        var dialog = new TestSqlRootSettingsDialog
        {
            Result = new SqlRootDialogResult(updated, IsNewRoot: false),
        };
        var review = new PatchReviewService(
            new FileHeldReviewStore(Path.Combine(directory, "review.json")));
        using var manager = new LoopManager(new TestRuntimeFactory());
        using var viewModel = new MainViewModel(
            new TestFolderPicker(),
            settings,
            new TestProfilesEditor(),
            new TestClipboardService(),
            dialog,
            new LoopLogStore(directory, 524_288, settings.Current),
            review,
            manager);

        await viewModel.ManageSqlRootsCommand.ExecuteAsync(null);

        Assert.Same(updated, viewModel.SelectedRoot);
        Assert.Equal(updated, Assert.Single(settings.Current.Roots));
        Assert.Equal("Selected SQL root Database.", viewModel.Status);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch
        {
        }
    }

    private sealed class TestSettingsStore(AppSettings settings) : ISettingsStore
    {
        public AppSettings Current { get; private set; } = settings;

        public AppSettings Load() => Current;

        public void Save(AppSettings settings) => Current = settings;
    }

    private sealed class TestFolderPicker : IFolderPicker
    {
        public string? PickFolder(string? initialDirectory = null) => null;
    }

    private sealed class TestProfilesEditor : IProfilesEditor
    {
        public void OpenForEdit()
        {
        }
    }

    private sealed class TestClipboardService : IClipboardService
    {
        public string? Text { get; private set; }

        public string? GetText() => Text;

        public void SetText(string text) => Text = text;
    }

    private sealed class TestSqlRootSettingsDialog : ISqlRootSettingsDialogService
    {
        public SqlRootDialogResult? Result { get; init; }

        public IReadOnlyList<RootProfile>? Roots { get; private set; }

        public RootProfile? SelectedRoot { get; private set; }

        public SqlRootDialogResult? Show(IEnumerable<RootProfile> roots, RootProfile? selectedRoot)
        {
            Roots = roots.ToArray();
            SelectedRoot = selectedRoot;
            return Result;
        }
    }

    private sealed class TestRuntimeFactory : ILoopRuntimeFactory
    {
        public LoopRuntimeBundle Create(ProcessingLoopViewModel loop, Action<LogEntry> log) =>
            new(new TestRuntime(), Context: null);
    }

    private sealed class TestRuntime : IMessageProcessingLoop
    {
        public bool IsRunning { get; private set; }

        public MessageProcessingLoopStatus Status { get; private set; } =
            MessageProcessingLoopStatus.Idle;

        public event EventHandler<LogEntry>? LogProduced
        {
            add { }
            remove { }
        }

        public event EventHandler<MessageProcessingLoopStatus>? StatusChanged;

        public event EventHandler? PatchInteractionChanged
        {
            add { }
            remove { }
        }

        public void MarkRequestIdSeen(string requestId)
        {
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            IsRunning = true;
            Status = MessageProcessingLoopStatus.Idle;
            StatusChanged?.Invoke(this, Status);
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            IsRunning = false;
            Status = MessageProcessingLoopStatus.Stopped;
            StatusChanged?.Invoke(this, Status);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
