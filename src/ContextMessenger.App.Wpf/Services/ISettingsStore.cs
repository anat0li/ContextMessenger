using ContextMessenger.App.Wpf.Settings;

namespace ContextMessenger.App.Wpf.Services;

public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}
