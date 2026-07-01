using System.Windows;
using ContextMessenger.App.Wpf.Logging;
using ContextMessenger.App.Wpf.Services;
using ContextMessenger.App.Wpf.ViewModels;
using Microsoft.Extensions.Logging;

namespace ContextMessenger.App.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var folderPicker = new WpfFolderPicker();
        var settings = new FileSettingsStore();
        var clipboard = new WpfClipboardService();
        var rootSwitchCoordinator = new AppRootSwitchCoordinator();
        var profilesProvider = new SettingsAvailableProfilesProvider(settings);
        var patchSessionStore = new SettingsPatchSessionStore(settings);
        var sessionFactory = new SessionFactory(profilesProvider, rootSwitchCoordinator, patchSessionStore);
        var profilesEditor = new NotepadProfilesEditor(settings);
        var loaded = settings.Load();
        var logStore = new LoopLogStore(loaded);
        var reviewService = new PatchReviewService(new FileHeldReviewStore());

        IChatWindowWatcher CreateWatcher(ILoggerFactory loggerFactory)
        {
            var scanner = new UiaChatScanner(loggerFactory.CreateLogger<UiaChatScanner>());
            return new ChatWindowPollingWatcher(scanner, loggerFactory.CreateLogger<ChatWindowPollingWatcher>());
        }

        var runtimeFactory = new LoopRuntimeFactory(sessionFactory, CreateWatcher, clipboard, loaded.Logging, reviewService);
        var loopManager = new LoopManager(runtimeFactory);
        var sqlConnectionTester = new SqlConnectionTester();
        var sqlRootSettingsDialog = new WpfSqlRootSettingsDialogService(sqlConnectionTester);

        var viewModel = new MainViewModel(
            folderPicker,
            settings,
            profilesEditor,
            clipboard,
            sqlRootSettingsDialog,
            logStore,
            reviewService,
            loopManager,
            rootSwitchCoordinator);

        Exit += (_, _) => viewModel.Dispose();

        var window = new MainWindow { DataContext = viewModel };
        MainWindow = window;
        window.Show();
    }
}
