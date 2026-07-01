using ContextMessenger.App.Wpf.Settings;
using ContextMessenger.App.Wpf.ViewModels;

namespace ContextMessenger.App.Wpf.Services;

public interface ISqlRootSettingsDialogService
{
    SqlRootDialogResult? Show(IEnumerable<RootProfile> roots, RootProfile? selectedRoot);
}
