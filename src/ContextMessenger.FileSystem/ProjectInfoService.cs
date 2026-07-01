using System.Text.Json;
using System.Xml.Linq;
using ContextMessenger.Core.ProjectInfo;

namespace ContextMessenger.FileSystem;

public sealed class ProjectInfoService : IProjectInfoService
{
    private const int MaxProjects = 500;
    private readonly PathSandbox _sandbox;
    private readonly IGitRepositoryInfoProvider _git;

    public ProjectInfoService(PathSandbox sandbox, IGitRepositoryInfoProvider? git = null)
    {
        _sandbox = sandbox ?? throw new ArgumentNullException(nameof(sandbox));
        _git = git ?? new LibGit2SharpGitRepositoryInfoProvider();
    }

    public ProjectInfo GetProjectInfo()
    {
        var solutionFiles = EnumerateFiles(["*.sln", "*.slnx"], int.MaxValue)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var projectFiles = EnumerateFiles(["*.csproj"], MaxProjects)
            .Select(ReadProjectInfo)
            .OrderBy(project => project.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ProjectInfo
        {
            RootPath = ".",
            SolutionFiles = solutionFiles,
            ProjectFiles = projectFiles,
            TestProjects = projectFiles
                .Where(project => project.IsTestProject)
                .Select(project => project.Path)
                .ToArray(),
            SdkVersion = ReadSdkVersion(),
            Git = _git.GetGitInfo(_sandbox.Root),
        };
    }

    private IEnumerable<string> EnumerateFiles(IReadOnlyCollection<string> filePatterns, int maxResults)
    {
        var count = 0;
        var stack = new Stack<string>();
        stack.Push(_sandbox.Root);

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
                if (DefaultExclusions.IsExcludedDirectoryName(Path.GetFileName(sub)))
                    continue;

                stack.Push(sub);
            }

            foreach (var file in files)
            {
                if (!MatchesAnyFilePattern(Path.GetFileName(file), filePatterns))
                    continue;

                yield return _sandbox.ToRelative(file);
                count++;
                if (count >= maxResults)
                    yield break;
            }
        }
    }

    private static bool MatchesAnyFilePattern(string fileName, IReadOnlyCollection<string> patterns) =>
        patterns.Any(pattern =>
            pattern.StartsWith("*", StringComparison.Ordinal) &&
            fileName.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase));

    private ProjectFileInfo ReadProjectInfo(string relativePath)
    {
        var targetFramework = default(string);
        var targetFrameworks = Array.Empty<string>();
        var outputType = default(string);
        var nullableValue = default(string);
        var langVersion = default(string);
        var projectReferences = Array.Empty<string>();
        var packages = Array.Empty<PackageReferenceInfo>();
        var isExplicitTestProject = false;
        var referencesTestSdk = false;

        try
        {
            var projectAbs = _sandbox.ResolveAbsolute(relativePath);
            var projectDirAbs = Path.GetDirectoryName(projectAbs) ?? _sandbox.Root;
            var doc = XDocument.Load(projectAbs, LoadOptions.None);
            targetFramework = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "TargetFramework")
                ?.Value
                .Trim();
            targetFrameworks = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "TargetFrameworks")
                ?.Value
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                ?? [];
            outputType = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "OutputType")
                ?.Value
                .Trim();
            nullableValue = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "Nullable")
                ?.Value
                .Trim();
            langVersion = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "LangVersion")
                ?.Value
                .Trim();
            isExplicitTestProject = doc.Descendants()
                .Any(e => e.Name.LocalName == "IsTestProject" &&
                          string.Equals(e.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase));
            referencesTestSdk = doc.Descendants()
                .Any(e => e.Name.LocalName == "PackageReference" &&
                          string.Equals((string?)e.Attribute("Include"), "Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase));
            projectReferences = doc.Descendants()
                .Where(e => e.Name.LocalName == "ProjectReference")
                .Select(e => (string?)e.Attribute("Include"))
                .Where(include => !string.IsNullOrWhiteSpace(include))
                .Select(include => ResolveProjectReference(projectDirAbs, include!))
                .Where(path => path is not null)
                .Select(path => path!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            packages = doc.Descendants()
                .Where(e => e.Name.LocalName == "PackageReference" && !string.IsNullOrWhiteSpace((string?)e.Attribute("Include")))
                .Select(e => new PackageReferenceInfo
                {
                    Name = ((string?)e.Attribute("Include"))!.Trim(),
                    Version = ReadPackageVersion(e),
                })
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
        }

        var normalized = relativePath.Replace('\\', '/');
        var fileName = Path.GetFileNameWithoutExtension(normalized);
        var isTestProject =
            normalized.Contains("/tests/", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase) ||
            isExplicitTestProject ||
            referencesTestSdk;

        return new ProjectFileInfo
        {
            Name = fileName,
            Path = normalized,
            TargetFramework = string.IsNullOrWhiteSpace(targetFramework) ? null : targetFramework,
            TargetFrameworks = targetFrameworks.Length == 0 ? null : targetFrameworks,
            OutputType = string.IsNullOrWhiteSpace(outputType) ? "Library" : outputType,
            Nullable = string.IsNullOrWhiteSpace(nullableValue) ? null : nullableValue,
            LangVersion = string.IsNullOrWhiteSpace(langVersion) ? null : langVersion,
            IsTestProject = isTestProject,
            ProjectReferences = projectReferences.Length == 0 ? null : projectReferences,
            Packages = packages.Length == 0 ? null : packages,
        };
    }

    private static string? ReadPackageVersion(XElement packageReference)
    {
        var attr = (string?)packageReference.Attribute("Version");
        if (!string.IsNullOrWhiteSpace(attr))
            return attr.Trim();

        var child = packageReference.Elements()
            .FirstOrDefault(c => c.Name.LocalName == "Version")
            ?.Value
            .Trim();
        return string.IsNullOrWhiteSpace(child) ? null : child;
    }

    private string? ResolveProjectReference(string projectDirAbs, string include)
    {
        try
        {
            var referenceAbs = Path.GetFullPath(Path.Combine(projectDirAbs, include));
            if (!_sandbox.IsInsideRoot(referenceAbs))
                return null;

            return _sandbox.ToRelative(referenceAbs);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (PathTooLongException)
        {
            return null;
        }
    }

    private string? ReadSdkVersion()
    {
        var globalJson = Path.Combine(_sandbox.Root, "global.json");
        if (!File.Exists(globalJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(globalJson));
            if (doc.RootElement.TryGetProperty("sdk", out var sdk) &&
                sdk.TryGetProperty("version", out var version) &&
                version.ValueKind == JsonValueKind.String)
            {
                return version.GetString();
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }
}
