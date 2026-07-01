using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using ContextMessenger.App.Wpf.Logging;
using ContextMessenger.App.Wpf.Settings;
using ContextMessenger.Core.Meta;

namespace ContextMessenger.App.Wpf.ViewModels;

public sealed partial class ProcessingLoopViewModel : ObservableObject
{
    private readonly string _timestampFormat;
    private readonly bool _showLogLevel;

    public ProcessingLoopViewModel(TargetProfile target, RootProfile root, LoggingSettings logging)
    {
        Target = target;
        Root = root;
        Status = "Idle";
        IsPatchReviewEnabled = root.Kind == RootKind.FileSystem && root.HoldPatchResponsesForReview;
        _timestampFormat = string.IsNullOrWhiteSpace(logging.LogTimestampFormat) ? "HH:mm:ss.fff" : logging.LogTimestampFormat;
        _showLogLevel = logging.ShowLogLevel;
    }

    public TargetProfile Target { get; }

    public RootProfile Root { get; }

    public string Title => $"{Target.Name} / {Root.Name}";

    public bool SupportsPatchReview => Root.Kind == RootKind.FileSystem;

    [ObservableProperty]
    private bool _isAutoProcessEnabled;

    /// <summary>
    /// Whether patch responses for this loop's root are held for manual review (vs sent
    /// automatically). Mirrors the per-root <c>HoldPatchResponsesForReview</c> setting.
    /// </summary>
    [ObservableProperty]
    private bool _isPatchReviewEnabled;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _status;

    [ObservableProperty]
    private string _logText = "";

    public ObservableCollection<LogEntry> Logs { get; } = new();

    public string LoadedLogText { get; private set; } = "";

    public void LoadLogLines(IEnumerable<string> lines)
    {
        LoadedLogText = string.Join(Environment.NewLine, lines);
        LogText = LoadedLogText;
    }

    /// <summary>
    /// Clears the in-memory log view. Loaded history and live text are cleared first, then
    /// <see cref="Logs"/> is reset, which the log view re-renders from (now empty).
    /// </summary>
    public void ClearLog()
    {
        LoadedLogText = "";
        LogText = "";
        Logs.Clear();
    }

    public void Append(LogEntry entry)
    {
        if (Logs.Count > 0)
        {
            var lastIndex = Logs.Count - 1;
            var last = Logs[lastIndex];
            if (last.Level == entry.Level &&
                last.Kind == entry.Kind &&
                string.Equals(last.Message, entry.Message, StringComparison.Ordinal))
            {
                Logs[lastIndex] = new LogEntry
                {
                    Timestamp = entry.Timestamp,
                    Level = entry.Level,
                    Kind = entry.Kind,
                    Message = entry.Message,
                    RepeatCount = last.RepeatCount + 1,
                };
                RebuildLogText();
                return;
            }
        }

        Logs.Add(entry);
        LogText = string.IsNullOrEmpty(LogText)
            ? FormatForDisplay(entry)
            : $"{LogText}{Environment.NewLine}{FormatForDisplay(entry)}";
    }

    private void RebuildLogText()
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrEmpty(LoadedLogText))
            builder.Append(LoadedLogText);

        foreach (var entry in Logs)
        {
            if (builder.Length > 0)
                builder.AppendLine();
            builder.Append(FormatForDisplay(entry));
        }
        LogText = builder.ToString();
    }

    public string FormatForDisplay(LogEntry entry)
    {
        var level = _showLogLevel ? $" [{entry.Level.ToString().ToUpperInvariant()}]" : "";
        var repeat = entry.RepeatCount > 1 ? $" (x{entry.RepeatCount})" : "";
        return $"{entry.Timestamp.ToString(_timestampFormat)}{level} [{entry.Kind}] {entry.Message}{repeat}";
    }

}
