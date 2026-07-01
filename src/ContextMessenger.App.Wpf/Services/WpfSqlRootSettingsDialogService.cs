using System.Windows;
using ContextMessenger.App.Wpf.Settings;
using ContextMessenger.App.Wpf.ViewModels;
using ContextMessenger.App.Wpf.Views;

namespace ContextMessenger.App.Wpf.Services;

public sealed class WpfSqlRootSettingsDialogService : ISqlRootSettingsDialogService
{
    private readonly ISqlConnectionTester _connectionTester;

    public WpfSqlRootSettingsDialogService(ISqlConnectionTester connectionTester)
    {
        _connectionTester = connectionTester ?? throw new ArgumentNullException(nameof(connectionTester));
    }

    public SqlRootDialogResult? Show(IEnumerable<RootProfile> roots, RootProfile? selectedRoot)
    {
        var viewModel = new SqlRootSettingsDialogViewModel(
            roots,
            selectedRoot,
            _connectionTester,
            message =>
                MessageBox.Show(
                    Application.Current?.MainWindow,
                    message,
                    "SQL root settings",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No) == MessageBoxResult.Yes,
            action => Application.Current?.Dispatcher.BeginInvoke(action));

        var dialog = new SqlRootSettingsDialog(viewModel)
        {
            Owner = Application.Current?.MainWindow,
        };

        return dialog.ShowDialog() == true ? viewModel.Result : null;
    }
}
