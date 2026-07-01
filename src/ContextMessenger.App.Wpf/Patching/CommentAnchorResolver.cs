using ContextMessenger.App.Wpf.ViewModels;
using System.Text.RegularExpressions;

namespace ContextMessenger.App.Wpf.Patching;

public sealed class CommentAnchorResolver
{
    public bool Reanchor(
        IEnumerable<ReviewComment> comments,
        IReadOnlyList<PatchReviewFile> files,
        Func<string, string?> getFileContent)
    {
        ArgumentNullException.ThrowIfNull(comments);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(getFileContent);

        var operations = files
            .GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Operation, StringComparer.OrdinalIgnoreCase);

        var changed = false;
        foreach (var comment in comments)
            changed |= Reanchor(comment, operations, getFileContent);

        return changed;
    }

    public CommentAnchorSnapshot Capture(string path, int line, string? content)
    {
        var lines = SplitLines(content);
        var index = line - 1;
        if (index < 0 || index >= lines.Count)
            return new CommentAnchorSnapshot("", [], []);

        return new CommentAnchorSnapshot(
            lines[index],
            lines.Skip(Math.Max(0, index - ContextRadius)).Take(index - Math.Max(0, index - ContextRadius)).ToArray(),
            lines.Skip(index + 1).Take(ContextRadius).ToArray());
    }

    private static bool Reanchor(
        ReviewComment comment,
        IReadOnlyDictionary<string, string> operations,
        Func<string, string?> getFileContent)
    {
        if (!comment.HasAnchor)
            return Apply(comment, comment.Line, CommentAnchorStatus.Current);

        if (operations.TryGetValue(comment.Path, out var operation) &&
            string.Equals(operation, "delete", StringComparison.OrdinalIgnoreCase))
        {
            return Apply(comment, comment.Line, CommentAnchorStatus.Deleted);
        }

        var lines = SplitLines(getFileContent(comment.Path));
        if (lines.Count == 0)
            return Apply(comment, comment.Line, CommentAnchorStatus.Missing);

        if (string.IsNullOrEmpty(comment.AnchorText))
        {
            var captured = CaptureAt(comment.Line, lines);
            comment.AnchorText = captured.AnchorText;
            comment.BeforeContext = captured.BeforeContext;
            comment.AfterContext = captured.AfterContext;
        }

        var originalIndex = comment.Line - 1;
        if (originalIndex >= 0 &&
            originalIndex < lines.Count &&
            string.Equals(lines[originalIndex], comment.AnchorText, StringComparison.Ordinal))
        {
            if (comment.AnchorStatus != CommentAnchorStatus.Current)
            {
                var mentionedMatch = BestMentionedIdentifierMatch(comment, lines);
                if (mentionedMatch is not null && mentionedMatch.Value != originalIndex)
                    return Apply(comment, mentionedMatch.Value + 1, CommentAnchorStatus.Moved);
            }

            return Apply(comment, comment.Line, CommentAnchorStatus.Current);
        }

        var match = BestExactTextMatch(comment, lines);
        if (match is not null)
            return Apply(comment, match.Value + 1, match.Value + 1 == comment.Line ? CommentAnchorStatus.Current : CommentAnchorStatus.Moved);

        var identifierMatch = BestMentionedIdentifierMatch(comment, lines);
        if (identifierMatch is not null)
            return Apply(comment, identifierMatch.Value + 1, CommentAnchorStatus.Moved);

        var contextMatch = BestContextOnlyMatch(comment, lines);
        if (contextMatch is not null)
            return Apply(comment, contextMatch.Value + 1, CommentAnchorStatus.Changed);

        return Apply(comment, comment.Line, CommentAnchorStatus.Changed);
    }

    private static CommentAnchorSnapshot CaptureAt(int line, IReadOnlyList<string> lines)
    {
        var index = line - 1;
        if (index < 0 || index >= lines.Count)
            return new CommentAnchorSnapshot("", [], []);

        return new CommentAnchorSnapshot(
            lines[index],
            lines.Skip(Math.Max(0, index - ContextRadius)).Take(index - Math.Max(0, index - ContextRadius)).ToArray(),
            lines.Skip(index + 1).Take(ContextRadius).ToArray());
    }

    private static int? BestExactTextMatch(ReviewComment comment, IReadOnlyList<string> lines)
    {
        var matches = lines
            .Select((line, index) => new { line, index })
            .Where(item => string.Equals(item.line, comment.AnchorText, StringComparison.Ordinal))
            .Select(item => new { item.index, Score = ContextScore(comment, lines, item.index) })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => Math.Abs(item.index + 1 - comment.Line))
            .ToArray();

        return matches.Length == 0 ? null : matches[0].index;
    }

    private static int? BestContextOnlyMatch(ReviewComment comment, IReadOnlyList<string> lines)
    {
        var candidates = lines
            .Select((_, index) => new { index, Score = ContextScore(comment, lines, index) })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => Math.Abs(item.index + 1 - comment.Line))
            .ToArray();

        return candidates.Length == 0 ? null : candidates[0].index;
    }

    private static int? BestMentionedIdentifierMatch(ReviewComment comment, IReadOnlyList<string> lines)
    {
        var identifiers = ExtractMentionedIdentifiers(comment);
        if (identifiers.Count == 0)
            return null;

        var candidates = lines
            .Select((line, index) => new
            {
                index,
                Score = identifiers.Sum(identifier => IdentifierScore(line, identifier.Key, identifier.Value)) +
                    ContextScore(comment, lines, index),
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => Math.Abs(item.index + 1 - comment.Line))
            .ToArray();

        return candidates.Length == 0 ? null : candidates[0].index;
    }

    private static IReadOnlyDictionary<string, int> ExtractMentionedIdentifiers(ReviewComment comment)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var message in comment.Messages)
        {
            foreach (Match match in IdentifierPattern.Matches(message.Text))
            {
                var identifier = match.Value;
                if (!IsUsefulIdentifier(identifier))
                    continue;

                result.TryGetValue(identifier, out var count);
                result[identifier] = count + 1;
            }
        }

        return result;
    }

    private static int IdentifierScore(string line, string identifier, int mentionCount)
    {
        if (!line.Contains(identifier, StringComparison.Ordinal))
            return 0;

        var weight = identifier.Contains('_', StringComparison.Ordinal) ? 10 : 3;
        if (line.Contains(" void ", StringComparison.Ordinal) ||
            line.Contains(" class ", StringComparison.Ordinal) ||
            line.Contains(" record ", StringComparison.Ordinal))
        {
            weight += 4;
        }

        return weight * mentionCount;
    }

    private static bool IsUsefulIdentifier(string value)
    {
        if (value.Length < 5 || CommonWords.Contains(value))
            return false;

        return value.Contains('_', StringComparison.Ordinal) ||
            value.Any(char.IsUpper) ||
            value.Length >= 10;
    }

    private static int ContextScore(ReviewComment comment, IReadOnlyList<string> lines, int index)
    {
        var score = 0;
        for (var i = 0; i < comment.BeforeContext.Count; i++)
        {
            var lineIndex = index - comment.BeforeContext.Count + i;
            if (lineIndex >= 0 && string.Equals(lines[lineIndex], comment.BeforeContext[i], StringComparison.Ordinal))
                score++;
        }

        for (var i = 0; i < comment.AfterContext.Count; i++)
        {
            var lineIndex = index + 1 + i;
            if (lineIndex < lines.Count && string.Equals(lines[lineIndex], comment.AfterContext[i], StringComparison.Ordinal))
                score++;
        }

        return score;
    }

    private static bool Apply(ReviewComment comment, int line, CommentAnchorStatus status)
    {
        var changed = false;
        if (comment.Line != line)
        {
            comment.Line = line;
            changed = true;
        }

        if (comment.AnchorStatus != status)
        {
            comment.AnchorStatus = status;
            changed = true;
        }

        return changed;
    }

    private static IReadOnlyList<string> SplitLines(string? content)
    {
        if (content is null)
            return [];

        var lines = content.Replace("\r\n", "\n").Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0)
            return lines[..^1];

        return lines;
    }

    private const int ContextRadius = 2;

    private static readonly Regex IdentifierPattern = new(
        @"\b[A-Za-z_][A-Za-z0-9_]*\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> CommonWords =
    [
        "After",
        "Anchor",
        "Check",
        "Current",
        "The",
        "This",
        "What",
        "around",
        "before",
        "changed",
        "comment",
        "content",
        "drift",
        "goal",
        "inserted",
        "line",
        "method",
        "original",
        "physical",
        "points",
        "probe",
        "reviewed",
        "source",
        "target",
        "testing",
    ];
}

public sealed record CommentAnchorSnapshot(
    string AnchorText,
    IReadOnlyList<string> BeforeContext,
    IReadOnlyList<string> AfterContext);
