namespace ContextMessenger.App.Wpf.Services;

public interface IFolderPicker
{
    string? PickFolder(string? initialDirectory = null);
}
