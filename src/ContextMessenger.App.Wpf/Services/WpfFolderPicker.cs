using System.IO;
using Microsoft.Win32;

namespace ContextMessenger.App.Wpf.Services;

public sealed class WpfFolderPicker : IFolderPicker
{
    public string? PickFolder(string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select repository root",
        };

        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
            dialog.InitialDirectory = initialDirectory;

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
