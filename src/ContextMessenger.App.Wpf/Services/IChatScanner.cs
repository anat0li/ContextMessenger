using ContextMessenger.App.Wpf.Settings;

namespace ContextMessenger.App.Wpf.Services;

/// <summary>
/// Scans the visible chat window for a configured target and returns trimmed
/// request bodies found between that target's configured request delimiters.
/// Bodies are the raw JSON between the delimiters.
/// </summary>
public interface IChatScanner
{
    IReadOnlyList<string> ScanForRequestBodies(TargetProfile target);
}
