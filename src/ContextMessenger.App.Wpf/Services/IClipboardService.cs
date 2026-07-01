namespace ContextMessenger.App.Wpf.Services;

public interface IClipboardService
{
    string? GetText();
    void SetText(string text);
}
