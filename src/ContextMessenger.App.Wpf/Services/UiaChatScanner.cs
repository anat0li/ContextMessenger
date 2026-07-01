using System.Windows.Automation;
using ContextMessenger.App.Wpf.Settings;
using ContextMessenger.Protocol;
using Microsoft.Extensions.Logging;

namespace ContextMessenger.App.Wpf.Services;

public sealed class UiaChatScanner : IChatScanner
{
    private const int RestoreSettleDelayMs = 100;

    private readonly ILogger<UiaChatScanner> _logger;

    public UiaChatScanner(ILogger<UiaChatScanner> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<string> ScanForRequestBodies(TargetProfile target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var automation = target.Automation;
        var processName = target.ProcessName;
        var process = WinAutomation.FindMainProcess(processName);
        if (process is null)
        {
            _logger.LogInformation(
                "No process found for target '{TargetName}' with process name '{ProcessName}'.",
                target.Name,
                processName);
            return Array.Empty<string>();
        }

        var hwnd = process.MainWindowHandle;
        NormalizeIfMinimized(hwnd, target);

        var root = AutomationElement.FromHandle(hwnd);
        var el = FindTextRoot(root, automation.RootAutomationId);
        if (el is null)
        {
            _logger.LogInformation(
                "Could not find '{RootAutomationId}' text root for target '{TargetName}' ({ProcessName}).",
                automation.RootAutomationId,
                target.Name,
                processName);
            return Array.Empty<string>();
        }

        var candidates = UiaChatTextReader.ReadCandidates(el);
        if (candidates.Count == 0)
        {
            _logger.LogInformation(
                "Could not read chat text for target '{TargetName}' ({ProcessName}) from '{RootAutomationId}'.",
                target.Name,
                processName,
                automation.RootAutomationId);
            return Array.Empty<string>();
        }

        RequestTextScanResult? bestResult = null;
        ChatTextReadResult? bestCandidate = null;
        foreach (var candidate in candidates)
        {
            var result = RequestTextScanner.Scan(
                candidate.Text,
                automation.MessageAnchorText.Values,
                automation.ResponseAnchorText.Values,
                automation.ReadyAnchorText.Values,
                automation.AnchorIgnoreIndex,
                automation.RequestBeginMarker,
                automation.RequestEndMarker,
                automation.RepairUnterminatedQuotes);

            if (result.Bodies.Count > 0)
            {
                _logger.LogInformation(
                    "Detected {RequestBodyCount} request block(s) for target '{TargetName}' ({ProcessName}) using {TextSource}.",
                    result.Bodies.Count,
                    target.Name,
                    processName,
                    candidate.Source);

                if (result.ReturnedInvalidBody)
                {
                    _logger.LogWarning(
                        "Request JSON candidate for target '{TargetName}' ({ProcessName}) using {TextSource} could not be parsed or validated. Error='{InvalidJsonMessage}'. Sending protocol error response.",
                        target.Name,
                        processName,
                        candidate.Source,
                        result.InvalidJsonMessage ?? "<none>");
                }

                return result.Bodies;
            }

            if (IsBetterDiagnostic(result, bestResult))
            {
                bestResult = result;
                bestCandidate = candidate;
            }
        }

        LogScanProblemIfAny(target, processName, bestCandidate, bestResult);
        return Array.Empty<string>();
    }

    private static AutomationElement? FindTextRoot(AutomationElement root, string automationId)
    {
        var elements = root.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));

        AutomationElement? candidate = null;
        for (var i = 0; i < elements.Count; i++)
        {
            if (!string.IsNullOrEmpty(elements[i].Current.Name))
                candidate = elements[i];
        }

        return candidate;
    }

    private void NormalizeIfMinimized(IntPtr hwnd, TargetProfile target)
    {
        if (!WinAutomation.IsIconic(hwnd)) return;

        _logger.LogInformation(
            "Restoring minimized window for target '{TargetName}' ({ProcessName}).",
            target.Name,
            target.ProcessName);
        WinAutomation.ShowWindow(hwnd, WinAutomation.SW_SHOWNOACTIVATE);
        Thread.Sleep(RestoreSettleDelayMs);
    }

    private void LogScanProblemIfAny(
        TargetProfile target,
        string processName,
        ChatTextReadResult? candidate,
        RequestTextScanResult? result)
    {
        if (candidate is null || result is null)
        {
            _logger.LogInformation(
                "No request text candidates found for target '{TargetName}' ({ProcessName}).",
                target.Name,
                processName);
            return;
        }

        if (result.HasInvalidJsonCandidate)
        {
            _logger.LogWarning(
                "Request JSON candidate for target '{TargetName}' ({ProcessName}) using {TextSource} could not be parsed or validated. Error='{InvalidJsonMessage}'.",
                target.Name,
                processName,
                candidate.Source,
                result.InvalidJsonMessage ?? "<none>");
            return;
        }

        if (!result.HasReadyAnchor)
        {
            _logger.LogInformation(
                "Could not find ready anchor for target '{TargetName}' ({ProcessName}) using {TextSource}. Anchor='{ReadyAnchorText}', TextLength={TextLength}.",
                target.Name,
                processName,
                candidate.Source,
                target.Automation.ReadyAnchorText.ToString(),
                result.TextLength);
            return;
        }

        if (result.HasBeginMarker || result.HasEndMarker)
        {
            // Invalid delimiter candidates are common in visible chat history
            // and should not be treated as protocol failures.
            return;
        }

        // Normal idle polling is intentionally silent. Repeated "no request"
        // messages make the loop log hard to read and add no operational value.
    }

    private static bool IsBetterDiagnostic(RequestTextScanResult current, RequestTextScanResult? best)
    {
        if (best is null)
            return true;

        return Score(current) > Score(best);

        static int Score(RequestTextScanResult result)
        {
            var score = 0;
            if (result.HasReadyAnchor) score += 4;
            if (result.HasBeginMarker) score += 2;
            if (result.HasEndMarker) score += 2;
            if (result.HasMessageAnchor) score += 1;
            return score;
        }
    }
}
