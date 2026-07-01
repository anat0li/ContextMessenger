using ContextMessenger.App.Wpf.ViewModels;
using System.Text.RegularExpressions;

namespace ContextMessenger.App.Wpf.Patching;

/// <summary>
/// Parses git unified-diff text into classified <see cref="DiffLine"/> rows for the inline diff
/// view. The leading +/-/space marker is stripped from content lines so the view can supply its
/// own gutter colouring; header and hunk lines are kept verbatim.
/// </summary>
public static class UnifiedDiffParser
{
    private static readonly Regex HunkHeader = new(
        @"^@@\s+-(?<old>\d+)(?:,\d+)?\s+\+(?<new>\d+)(?:,\d+)?\s+@@",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] HeaderPrefixes =
    [
        "diff ", "index ", "new file", "deleted file", "old mode", "new mode",
        "similarity index", "dissimilarity index", "rename ", "copy ", "Binary files",
    ];

    public static IReadOnlyList<DiffLine> Parse(string? unifiedDiff)
    {
        if (string.IsNullOrEmpty(unifiedDiff))
            return [];

        var lines = unifiedDiff.Replace("\r\n", "\n").Split('\n');
        var result = new List<DiffLine>(lines.Length);
        int? oldLine = null;
        int? newLine = null;

        foreach (var line in lines)
        {
            var diffLine = Classify(line, ref oldLine, ref newLine);
            result.Add(diffLine);
        }

        // A trailing newline produces a final empty element; drop it so the view has no blank tail.
        if (result.Count > 0 && result[^1].Kind == DiffLineKind.Context && result[^1].Text.Length == 0)
            result.RemoveAt(result.Count - 1);

        return result;
    }

    private static DiffLine Classify(string line, ref int? oldLine, ref int? newLine)
    {
        if (line.StartsWith("@@", StringComparison.Ordinal))
        {
            var match = HunkHeader.Match(line);
            if (match.Success)
            {
                oldLine = int.Parse(match.Groups["old"].Value);
                newLine = int.Parse(match.Groups["new"].Value);
            }
            else
            {
                oldLine = null;
                newLine = null;
            }

            return new DiffLine { Kind = DiffLineKind.Hunk, Text = line };
        }

        // File markers "--- a/x" / "+++ b/x" are headers, not removed/added content.
        if (line.StartsWith("+++", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal))
            return new DiffLine { Kind = DiffLineKind.Header, Text = line };

        if (IsHeader(line))
            return new DiffLine { Kind = DiffLineKind.Header, Text = line };

        if (line.StartsWith('+'))
        {
            var currentNewLine = newLine;
            if (newLine.HasValue)
                newLine++;
            return new DiffLine { Kind = DiffLineKind.Added, Text = line[1..], NewLineNumber = currentNewLine };
        }

        if (line.StartsWith('-'))
        {
            var currentOldLine = oldLine;
            if (oldLine.HasValue)
                oldLine++;
            return new DiffLine { Kind = DiffLineKind.Removed, Text = line[1..], OldLineNumber = currentOldLine };
        }

        if (line.StartsWith(' '))
        {
            var currentOldLine = oldLine;
            var currentNewLine = newLine;
            if (oldLine.HasValue)
                oldLine++;
            if (newLine.HasValue)
                newLine++;
            return new DiffLine
            {
                Kind = DiffLineKind.Context,
                Text = line[1..],
                OldLineNumber = currentOldLine,
                NewLineNumber = currentNewLine,
            };
        }

        // Blank separators and "\ No newline at end of file" markers.
        return new DiffLine { Kind = DiffLineKind.Context, Text = line };
    }

    private static bool IsHeader(string line)
    {
        foreach (var prefix in HeaderPrefixes)
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
