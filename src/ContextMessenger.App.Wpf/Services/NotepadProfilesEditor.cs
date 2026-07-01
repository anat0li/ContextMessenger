using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace ContextMessenger.App.Wpf.Services;

public sealed class NotepadProfilesEditor : IProfilesEditor
{
    private readonly FileSettingsStore _settingsStore;

    public NotepadProfilesEditor(FileSettingsStore settingsStore)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
    }

    public void OpenForEdit()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = Quote(_settingsStore.SettingsPath),
                UseShellExecute = true,
            });
        }
        catch (Win32Exception)
        {
            var path = _settingsStore.SettingsPath;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,{Quote(path)}",
                UseShellExecute = true,
            });
        }
    }

    private static string Quote(string value) => $"\"{value}\"";
}
