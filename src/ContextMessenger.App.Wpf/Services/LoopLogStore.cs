using System.IO;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using ContextMessenger.App.Wpf.Logging;
using ContextMessenger.App.Wpf.Settings;
using Microsoft.Extensions.Logging;

namespace ContextMessenger.App.Wpf.Services;

public sealed class LoopLogStore
{
    private static readonly Regex UnsafeFileNameChars = new($"[{Regex.Escape(new string(Path.GetInvalidFileNameChars()))}]+", RegexOptions.Compiled);
    private static readonly Regex FormattedLine = new(
        @"^(?<timestamp>.+?)\s+(?:\[(?<level>[A-Z]+)\]\s+)?\[(?<kind>[^\]]+)\]\s+(?<message>.*?)(?:\s+\(x(?<count>\d+)\))?$",
        RegexOptions.Compiled);

    private readonly string _logDirectory;
    private readonly long _maxLogFileBytes;
    private readonly string _timestampFormat;
    private readonly bool _showLogLevel;
    private readonly bool _enableFileLogging;
    private readonly bool _enableDebugOutputLogging;
    private readonly Dictionary<string, List<LogEntry>> _sessionEntriesByPath = new(StringComparer.OrdinalIgnoreCase);

    public LoopLogStore()
        : this(DefaultLogDirectory(), new AppSettings().Logging.MaxFileBytes)
    {
    }

    public LoopLogStore(long maxLogFileBytes)
        : this(DefaultLogDirectory(), maxLogFileBytes)
    {
    }

    public LoopLogStore(string logDirectory, long maxLogFileBytes)
        : this(logDirectory, maxLogFileBytes, new AppSettings())
    {
    }

    public LoopLogStore(AppSettings settings)
        : this(DefaultLogDirectory(), settings.Logging.MaxFileBytes, settings)
    {
    }

    public LoopLogStore(string logDirectory, long maxLogFileBytes, AppSettings settings)
    {
        _logDirectory = logDirectory;
        _maxLogFileBytes = Math.Max(1024, settings.Logging.MaxFileBytes > 0 ? settings.Logging.MaxFileBytes : maxLogFileBytes);
        _timestampFormat = string.IsNullOrWhiteSpace(settings.Logging.LogFileTimestampFormat)
            ? "yyyy-MM-dd HH:mm:ss"
            : settings.Logging.LogFileTimestampFormat;
        _showLogLevel = settings.Logging.ShowLogLevel;
        _enableFileLogging = settings.Logging.EnableFileLogging;
        _enableDebugOutputLogging = settings.Logging.EnableDebugOutputLogging;
    }

    public IReadOnlyList<string> Load(string targetName, string rootName)
    {
        var path = GetPath(targetName, rootName);
        if (!File.Exists(path)) return [];

        return ConsolidateLoadedLines(File.ReadLines(path)).ToArray();
    }

    public void Append(string targetName, string rootName, LogEntry entry)
    {
        var line = Format(entry);
        if (_enableDebugOutputLogging)
            Debug.WriteLine(line);

        if (!_enableFileLogging)
            return;

        Directory.CreateDirectory(_logDirectory);
        var path = GetPath(targetName, rootName);
        RotateIfNeeded(path);

        var sessionEntries = GetSessionEntries(path);
        if (sessionEntries.Count > 0 && CanConsolidate(sessionEntries[^1], entry))
        {
            sessionEntries[^1] = Consolidate(sessionEntries[^1], entry);
            RewriteSessionTail(path, sessionEntries);
            return;
        }

        sessionEntries.Add(entry);
        var prefix = File.Exists(path) && new FileInfo(path).Length > 0
            ? EntrySeparator()
            : "";
        File.AppendAllText(path, prefix + line + Environment.NewLine, Encoding.UTF8);
    }

    private string GetPath(string targetName, string rootName) =>
        Path.Combine(_logDirectory, $"{Sanitize(targetName)}-{Sanitize(rootName)}.log");

    /// <summary>
    /// Archives the current log file for the (target, root) loop to the next free
    /// <c>{stem}{index}.log</c> name and resets the in-memory session state, so the next
    /// append starts a fresh file. No-op when no log file exists yet.
    /// </summary>
    public void Clear(string targetName, string rootName)
    {
        var path = GetPath(targetName, rootName);
        if (File.Exists(path))
            ArchiveToNextIndex(path);

        _sessionEntriesByPath.Remove(path);
    }

    private void RotateIfNeeded(string path)
    {
        if (!File.Exists(path)) return;
        if (new FileInfo(path).Length < _maxLogFileBytes) return;

        ArchiveToNextIndex(path);
    }

    private static void ArchiveToNextIndex(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var index = 1;
        string rotatedPath;
        do
        {
            rotatedPath = Path.Combine(directory, $"{stem}{index}{extension}");
            index++;
        }
        while (File.Exists(rotatedPath));

        File.Move(path, rotatedPath);
    }

    private string Format(LogEntry entry)
    {
        var level = _showLogLevel ? $" [{entry.Level.ToString().ToUpperInvariant()}]" : "";
        var repeat = entry.RepeatCount > 1 ? $" (x{entry.RepeatCount})" : "";
        return $"{entry.Timestamp.ToString(_timestampFormat)}{level} [{entry.Kind}] {entry.Message}{repeat}";
    }

    private List<LogEntry> GetSessionEntries(string path)
    {
        if (_sessionEntriesByPath.TryGetValue(path, out var entries))
            return entries;

        entries = [];
        _sessionEntriesByPath.Add(path, entries);
        return entries;
    }

    private void RewriteSessionTail(string path, IReadOnlyList<LogEntry> sessionEntries)
    {
        var existingLines = File.Exists(path)
            ? File.ReadAllLines(path).ToList()
            : [];
        var sessionText = FormatEntries(sessionEntries);
        var sessionLineCount = sessionText.Split(Environment.NewLine).Length;
        var prefixLines = existingLines.Count > sessionLineCount
            ? existingLines.Take(existingLines.Count - sessionLineCount).ToArray()
            : [];

        var builder = new StringBuilder();
        if (prefixLines.Length > 0)
        {
            builder.Append(string.Join(Environment.NewLine, prefixLines).TrimEnd());
            builder.Append(EntrySeparator());
        }
        builder.Append(sessionText);
        builder.AppendLine();
        File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
    }

    private string FormatEntries(IEnumerable<LogEntry> entries) =>
        string.Join(EntrySeparator(), entries.Select(Format));

    private string EntrySeparator() =>
        Environment.NewLine;

    private static bool CanConsolidate(LogEntry existing, LogEntry next) =>
        existing.Level == next.Level &&
        existing.Kind == next.Kind &&
        string.Equals(existing.Message, next.Message, StringComparison.Ordinal);

    private static LogEntry Consolidate(LogEntry existing, LogEntry next) => new()
    {
        Timestamp = next.Timestamp,
        Level = next.Level,
        Kind = next.Kind,
        Message = next.Message,
        RepeatCount = existing.RepeatCount + 1,
    };

    private IEnumerable<string> ConsolidateLoadedLines(IEnumerable<string> lines)
    {
        var entries = new List<ParsedLine>();
        foreach (var line in lines.Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            var parsed = TryParseLine(line);
            if (parsed is not null &&
                entries.Count > 0 &&
                entries[^1].CanConsolidateWith(parsed))
            {
                entries[^1] = entries[^1].ConsolidateWith(parsed);
                continue;
            }

            entries.Add(parsed ?? ParsedLine.Raw(line));
        }

        return entries.Select(entry => entry.Text);
    }

    private ParsedLine? TryParseLine(string line)
    {
        var match = FormattedLine.Match(line);
        if (!match.Success)
            return null;

        var level = match.Groups["level"].Success ? match.Groups["level"].Value : "";
        var kind = match.Groups["kind"].Value;
        var message = match.Groups["message"].Value;
        var count = match.Groups["count"].Success && int.TryParse(match.Groups["count"].Value, out var parsedCount)
            ? parsedCount
            : 1;

        return new ParsedLine(line, match.Groups["timestamp"].Value, level, kind, message, count);
    }

    private sealed record ParsedLine(
        string Text,
        string Timestamp,
        string Level,
        string Kind,
        string Message,
        int Count)
    {
        public static ParsedLine Raw(string text) => new(text, "", "", "", text, 1);

        public bool CanConsolidateWith(ParsedLine other) =>
            !string.IsNullOrEmpty(Kind) &&
            string.Equals(Level, other.Level, StringComparison.Ordinal) &&
            string.Equals(Kind, other.Kind, StringComparison.Ordinal) &&
            string.Equals(Message, other.Message, StringComparison.Ordinal);

        public ParsedLine ConsolidateWith(ParsedLine other)
        {
            var level = string.IsNullOrEmpty(other.Level) ? "" : $" [{other.Level}]";
            var count = Count + other.Count;
            var text = $"{other.Timestamp}{level} [{other.Kind}] {other.Message} (x{count})";
            return this with
            {
                Text = text,
                Timestamp = other.Timestamp,
                Count = count,
            };
        }
    }

    private static string Sanitize(string value)
    {
        var sanitized = UnsafeFileNameChars.Replace(value, "_").Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "unnamed" : sanitized;
    }

    private static string DefaultLogDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ContextMessenger",
        "logs");
}
