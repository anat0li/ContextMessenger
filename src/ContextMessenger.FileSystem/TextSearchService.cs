using System.Text.RegularExpressions;
using ContextMessenger.Core.FileSystem;
using Microsoft.Extensions.FileSystemGlobbing;

namespace ContextMessenger.FileSystem;

public sealed class TextSearchService
{
    private readonly PathSandbox _sandbox;

    public TextSearchService(PathSandbox sandbox)
    {
        _sandbox = sandbox ?? throw new ArgumentNullException(nameof(sandbox));
    }

    public IReadOnlyList<SearchMatch> SearchText(SearchQuery query)
    {
        if (string.IsNullOrEmpty(query.Pattern))
            throw new ArgumentException("Pattern must be non-empty.", nameof(query));
        if (query.MaxResults <= 0)
            throw new ArgumentOutOfRangeException(nameof(query), "MaxResults must be positive.");

        var startAbs = ResolveDirectory(query.RelativePath);
        var matcher = GlobMatcherFactory.Build(query.IncludeGlobs, query.ExcludeGlobs);
        var excludedDirectoryNames = GlobMatcherFactory.GetDirectoryExcludeNames(query.ExcludeGlobs);
        var results = new List<SearchMatch>();

        if (query.IsRegex)
        {
            var options = RegexOptions.Compiled;
            if (query.IgnoreCase) options |= RegexOptions.IgnoreCase;
            var regex = new Regex(query.Pattern, options);
            foreach (var (abs, rel) in EnumerateMatchingFiles(startAbs, matcher, excludedDirectoryNames))
            {
                if (results.Count >= query.MaxResults) break;
                ScanFileRegex(abs, rel, regex, results, query.MaxResults);
            }
        }
        else
        {
            var cmp = query.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            foreach (var (abs, rel) in EnumerateMatchingFiles(startAbs, matcher, excludedDirectoryNames))
            {
                if (results.Count >= query.MaxResults) break;
                ScanFileLiteral(abs, rel, query.Pattern, cmp, results, query.MaxResults);
            }
        }

        return results;
    }

    public IReadOnlyList<string> ListFiles(ListFilesQuery query)
    {
        if (query.MaxResults <= 0)
            throw new ArgumentOutOfRangeException(nameof(query), "MaxResults must be positive.");

        var startAbs = ResolveDirectory(query.RelativePath);
        var matcher = GlobMatcherFactory.Build(query.IncludeGlobs, query.ExcludeGlobs);
        var excludedDirectoryNames = GlobMatcherFactory.GetDirectoryExcludeNames(query.ExcludeGlobs);
        var results = new List<string>();

        foreach (var (_, rel) in EnumerateMatchingFiles(startAbs, matcher, excludedDirectoryNames))
        {
            if (results.Count >= query.MaxResults) break;
            results.Add(rel);
        }
        results.Sort(StringComparer.OrdinalIgnoreCase);
        return results;
    }

    private string ResolveDirectory(string relativePath)
    {
        var abs = _sandbox.ResolveAbsolute(relativePath);
        if (!Directory.Exists(abs))
            throw new DirectoryNotFoundException($"Path is not a directory: {relativePath}");
        return abs;
    }

    private IEnumerable<(string Abs, string Rel)> EnumerateMatchingFiles(
        string startDirAbs,
        Matcher matcher,
        IReadOnlySet<string> excludedDirectoryNames)
    {
        var stack = new Stack<string>();
        stack.Push(startDirAbs);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            string[] subDirs, files;
            try
            {
                subDirs = Directory.GetDirectories(dir);
                files = Directory.GetFiles(dir);
            }
            catch (UnauthorizedAccessException) { continue; }
            catch (DirectoryNotFoundException) { continue; }

            Array.Sort(subDirs, StringComparer.OrdinalIgnoreCase);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            for (var i = subDirs.Length - 1; i >= 0; i--)
            {
                var sub = subDirs[i];
                var subName = Path.GetFileName(sub);
                if (DefaultExclusions.IsExcludedDirectoryName(subName)
                    || excludedDirectoryNames.Contains(subName)) continue;
                stack.Push(sub);
            }

            foreach (var file in files)
            {
                var rel = _sandbox.ToRelative(file);
                var matchRel = ToRelativePath(startDirAbs, file);
                if (!matcher.Match(startDirAbs, matchRel).HasMatches) continue;
                yield return (file, rel);
            }
        }
    }

    private static string ToRelativePath(string rootAbs, string fileAbs) =>
        Path.GetRelativePath(rootAbs, fileAbs).Replace(Path.DirectorySeparatorChar, '/');

    private static void ScanFileLiteral(
        string abs, string rel, string pattern, StringComparison cmp,
        List<SearchMatch> results, int max)
    {
        var lineNumber = 0;
        IEnumerable<string> lines;
        try { lines = File.ReadLines(abs); }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }

        try
        {
            foreach (var line in lines)
            {
                lineNumber++;
                var idx = line.IndexOf(pattern, cmp);
                if (idx < 0) continue;
                results.Add(new SearchMatch(rel, lineNumber, line, idx, idx + pattern.Length));
                if (results.Count >= max) return;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void ScanFileRegex(
        string abs, string rel, Regex regex,
        List<SearchMatch> results, int max)
    {
        var lineNumber = 0;
        IEnumerable<string> lines;
        try { lines = File.ReadLines(abs); }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }

        try
        {
            foreach (var line in lines)
            {
                lineNumber++;
                var m = regex.Match(line);
                if (!m.Success) continue;
                results.Add(new SearchMatch(rel, lineNumber, line, m.Index, m.Index + m.Length));
                if (results.Count >= max) return;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
