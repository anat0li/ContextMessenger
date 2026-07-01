using Microsoft.Extensions.FileSystemGlobbing;

namespace ContextMessenger.FileSystem;

internal static class GlobMatcherFactory
{
    public static Matcher Build(
        IReadOnlyList<string> includes,
        IReadOnlyList<string> excludes,
        bool addDefaultDirectoryExclusions = true)
    {
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);

        if (includes.Count == 0)
            matcher.AddInclude("**/*");
        else
            foreach (var pattern in ExpandPatterns(includes)) matcher.AddInclude(pattern);

        if (addDefaultDirectoryExclusions)
            foreach (var pattern in ExpandPatterns(DefaultExclusions.Globs)) matcher.AddExclude(pattern);

        foreach (var pattern in ExpandPatterns(excludes)) matcher.AddExclude(pattern);

        return matcher;
    }

    public static IReadOnlySet<string> GetDirectoryExcludeNames(IEnumerable<string> patterns)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pattern in patterns)
        {
            var name = TryGetDirectoryExcludeName(NormalizePattern(pattern));
            if (name is not null)
                names.Add(name);
        }

        return names;
    }

    private static IEnumerable<string> ExpandPatterns(IEnumerable<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            var normalized = NormalizePattern(pattern);
            if (normalized.Length == 0)
                continue;

            yield return normalized;

            if (normalized.StartsWith("**/", StringComparison.Ordinal))
            {
                yield return normalized[3..];
                continue;
            }

            if (!normalized.StartsWith("**/", StringComparison.Ordinal))
                yield return "**/" + normalized;
        }
    }

    private static string NormalizePattern(string pattern)
    {
        var normalized = pattern.Trim().Replace('\\', '/');

        while (normalized.StartsWith("/", StringComparison.Ordinal))
            normalized = normalized[1..];

        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];

        // Some chat UIs can render markdown glob text like **/*.cs as /.cs.
        // Treat bare extension text as the intended recursive extension glob.
        if (normalized.StartsWith(".", StringComparison.Ordinal)
            && !normalized.Contains('/', StringComparison.Ordinal)
            && !normalized.Contains('*', StringComparison.Ordinal)
            && !normalized.Contains('?', StringComparison.Ordinal))
            normalized = "**/*" + normalized;

        if (pattern.StartsWith("/", StringComparison.Ordinal) && normalized.StartsWith("*.", StringComparison.Ordinal))
            normalized = "**/" + normalized;

        return normalized;
    }

    private static string? TryGetDirectoryExcludeName(string pattern)
    {
        var normalized = pattern.TrimEnd('/');

        if (normalized.EndsWith("/**", StringComparison.Ordinal))
            normalized = normalized[..^3].TrimEnd('/');

        if (normalized.StartsWith("**/", StringComparison.Ordinal))
            normalized = normalized[3..];

        if (normalized.Length == 0
            || normalized.Contains('/', StringComparison.Ordinal)
            || normalized.Contains('*', StringComparison.Ordinal)
            || normalized.Contains('?', StringComparison.Ordinal))
            return null;

        return normalized;
    }
}
