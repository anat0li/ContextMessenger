using System.Security.Cryptography;
using System.Text;
using ContextMessenger.Core.FileSystem;

namespace ContextMessenger.FileSystem;

public sealed class FileReaderService
{
    private readonly PathSandbox _sandbox;

    public FileReaderService(PathSandbox sandbox)
    {
        _sandbox = sandbox ?? throw new ArgumentNullException(nameof(sandbox));
    }

    public FileContent ReadFile(
        string relativePath,
        int? startLine = null,
        int? endLine = null,
        long maxBytes = 1_048_576)
    {
        if (maxBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytes), "maxBytes must be positive.");
        if (startLine is < 1)
            throw new ArgumentOutOfRangeException(nameof(startLine), "startLine is 1-based.");
        if (endLine is < 1)
            throw new ArgumentOutOfRangeException(nameof(endLine), "endLine is 1-based.");
        if (startLine.HasValue && endLine.HasValue && endLine.Value < startLine.Value)
            throw new ArgumentException("endLine must be >= startLine.", nameof(endLine));

        var abs = _sandbox.ResolveAbsolute(relativePath);
        if (!File.Exists(abs))
            throw new FileNotFoundException($"File not found: {relativePath}", abs);

        var fileSize = new FileInfo(abs).Length;
        var rel = _sandbox.ToRelative(abs);

        if (startLine.HasValue || endLine.HasValue)
            return ReadRange(abs, rel, fileSize, startLine ?? 1, endLine);

        return fileSize <= maxBytes
            ? ReadFull(abs, rel, fileSize)
            : ReadTruncated(abs, rel, fileSize, maxBytes);
    }

    private static FileContent ReadRange(string abs, string rel, long fileSize, int startLine, int? endLine)
    {
        var content = File.ReadAllText(abs);
        var lineSpans = GetLineSpans(content);
        var startIdx = startLine - 1;

        if (startIdx >= lineSpans.Count)
            return new FileContent(
                rel,
                "",
                0,
                fileSize,
                IsTruncated: false,
                ContentHash: ComputeContentHash(abs),
                RangeHash: ComputeHash(""),
                RangeStartLine: startLine,
                RangeEndLine: startLine - 1,
                RangeIncludesEndLineTerminator: false,
                LineEnding: DominantLineEnding(content));

        var endIdx = endLine.HasValue ? Math.Min(endLine.Value, lineSpans.Count) - 1 : lineSpans.Count - 1;
        var startOffset = lineSpans[startIdx].Start;
        var endSpan = lineSpans[endIdx];
        var endOffset = endSpan.EndExclusive;
        var slice = content[startOffset..endOffset];

        return new FileContent(
            rel,
            slice,
            endIdx - startIdx + 1,
            fileSize,
            IsTruncated: false,
            ContentHash: ComputeContentHash(abs),
            RangeHash: ComputeHash(slice),
            RangeStartLine: startLine,
            RangeEndLine: startLine + endIdx - startIdx,
            RangeIncludesEndLineTerminator: endSpan.IncludesTerminator,
            LineEnding: DominantLineEnding(content));
    }

    private static FileContent ReadFull(string abs, string rel, long fileSize)
    {
        var content = File.ReadAllText(abs);
        return new FileContent(rel, content, CountLines(content), fileSize, IsTruncated: false, ContentHash: ComputeContentHash(abs));
    }

    private static FileContent ReadTruncated(string abs, string rel, long fileSize, long maxBytes)
    {
        using var fs = File.OpenRead(abs);
        var capped = (int)Math.Min(maxBytes, int.MaxValue);
        var buf = new byte[capped];
        var read = fs.Read(buf, 0, capped);
        var content = System.Text.Encoding.UTF8.GetString(buf, 0, read);
        return new FileContent(rel, content, CountLines(content), fileSize, IsTruncated: true, ContentHash: ComputeContentHash(abs));
    }

    private static string ComputeContentHash(string abs)
    {
        using var stream = File.OpenRead(abs);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string ComputeHash(string text) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static int CountLines(string content)
    {
        if (content.Length == 0) return 0;
        var newlines = 0;
        foreach (var c in content)
            if (c == '\n') newlines++;
        return content[^1] == '\n' ? newlines : newlines + 1;
    }

    private static List<LineSpan> GetLineSpans(string content)
    {
        var spans = new List<LineSpan>();
        var start = 0;
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] == '\r')
            {
                var end = i + 1;
                if (end < content.Length && content[end] == '\n')
                    end++;
                spans.Add(new LineSpan(start, end, IncludesTerminator: true));
                start = end;
                i = end - 1;
            }
            else if (content[i] == '\n')
            {
                spans.Add(new LineSpan(start, i + 1, IncludesTerminator: true));
                start = i + 1;
            }
        }

        if (start < content.Length)
            spans.Add(new LineSpan(start, content.Length, IncludesTerminator: false));

        return spans;
    }

    private static string? DominantLineEnding(string text)
    {
        var crlf = 0;
        var lf = 0;
        var cr = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                {
                    cr++;
                }
            }
            else if (text[i] == '\n')
            {
                lf++;
            }
        }

        if (crlf == 0 && lf == 0 && cr == 0)
            return null;
        if (crlf >= lf && crlf >= cr)
            return "crlf";
        return lf >= cr ? "lf" : "cr";
    }

    private readonly record struct LineSpan(int Start, int EndExclusive, bool IncludesTerminator);
}
