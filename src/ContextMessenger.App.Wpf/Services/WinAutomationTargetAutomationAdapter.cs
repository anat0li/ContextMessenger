using ContextMessenger.App.Wpf.Settings;
using Microsoft.Extensions.Logging;

namespace ContextMessenger.App.Wpf.Services;

public sealed class WinAutomationTargetAutomationAdapter : ITargetAutomationAdapter
{
    private readonly TargetProfile _target;
    private readonly IClipboardService _clipboard;
    private readonly ILogger _logger;

    public WinAutomationTargetAutomationAdapter(
        TargetProfile target,
        IClipboardService clipboard,
        ILogger logger)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> SubmitResponseAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _clipboard.SetText(text);

        var submitted = await WinAutomation.PasteIntoChatInput(_target, submit: true, _logger);
        cancellationToken.ThrowIfCancellationRequested();
        return submitted;
    }
}
