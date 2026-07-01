using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace ContextMessenger.App.Wpf.Services;

public sealed class WpfClipboardService : IClipboardService
{
    private const int MaxAttempts = 5;
    private const int DelayMs = 50;
    private readonly Dispatcher _dispatcher;

    public WpfClipboardService()
        : this(Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher)
    {
    }

    internal WpfClipboardService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public string? GetText() => InvokeOnClipboardThread(GetTextCore);

    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        InvokeOnClipboardThread(() =>
        {
            SetTextCore(text);
            return true;
        });
    }

    private string? GetTextCore()
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (COMException) when (attempt < MaxAttempts - 1)
            {
                Thread.Sleep(DelayMs);
            }
            catch (ExternalException) when (attempt < MaxAttempts - 1)
            {
                Thread.Sleep(DelayMs);
            }
        }
        return null;
    }

    private static void SetTextCore(string text)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return;
            }
            catch (COMException) when (attempt < MaxAttempts - 1)
            {
                Thread.Sleep(DelayMs);
            }
            catch (ExternalException) when (attempt < MaxAttempts - 1)
            {
                Thread.Sleep(DelayMs);
            }
        }

        throw new InvalidOperationException(
            $"Could not write to the clipboard after {MaxAttempts} attempts.");
    }

    private T InvokeOnClipboardThread<T>(Func<T> action)
    {
        if (_dispatcher.CheckAccess())
            return action();

        return _dispatcher.Invoke(action);
    }
}
