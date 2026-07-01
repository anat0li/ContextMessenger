using System.Windows;
using ContextMessenger.App.Wpf.ViewModels;

namespace ContextMessenger.App.Wpf.Views;

public partial class SqlRootSettingsDialog : Window
{
    public SqlRootSettingsDialog(SqlRootSettingsDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Completed += OnCompleted;
    }

    private void OnCompleted(object? sender, EventArgs e)
    {
        DialogResult = true;
    }
}
