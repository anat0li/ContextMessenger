using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ContextMessenger.Core.Patching;

namespace ContextMessenger.Patching;

public sealed class DotnetBuildRunner : IBuildRunner
{
    private const int MaxCapturedOutputChars = 1200;

    private static readonly Regex DiagnosticRegex = new(
        @"^(?<path>.*)\((?<line>\d+),(?<column>\d+)\):\s(?<kind>error|warning)\s(?<code>[A-Z]+\d+):\s(?<message>.*?)(?:\s\[(?<project>.*)\])?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly string _rootPath;

    public DotnetBuildRunner(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Root path is required.", nameof(rootPath));

        _rootPath = Path.GetFullPath(rootPath);
    }

    public BuildResult Run(BuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var buildPath = ResolveBuildPath(request.Path);
        var timeoutSeconds = request.TimeoutSeconds > 0 ? request.TimeoutSeconds : 120;
        var artifactsPath = CreateArtifactsPath(buildPath, request.Configuration);
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stopwatch = Stopwatch.StartNew();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = _rootPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };

        AddBuildArguments(process.StartInfo, buildPath, request, artifactsPath);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                stdout.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                stderr.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var exited = process.WaitForExit(timeoutSeconds * 1000);
        if (!exited)
        {
            TryKill(process);
            stopwatch.Stop();
            var timedOutStdout = LimitOutput(stdout.ToString());
            var timedOutStderr = LimitOutput(stderr.ToString());
            return new BuildResult
            {
                Status = "timeout",
                Path = NormalizePath(buildPath),
                Configuration = request.Configuration,
                DurationMs = (int)stopwatch.ElapsedMilliseconds,
                Stdout = timedOutStdout.Text,
                StdoutTruncated = timedOutStdout.Truncated,
                Stderr = timedOutStderr.Text,
                StderrTruncated = timedOutStderr.Truncated,
            };
        }

        process.WaitForExit();
        stopwatch.Stop();

        var outText = stdout.ToString();
        var errText = stderr.ToString();
        var diagnostics = ParseDiagnostics(outText + errText);
        var limitedStdout = LimitOutput(outText);
        var limitedStderr = LimitOutput(errText);
        return new BuildResult
        {
            Status = process.ExitCode == 0 ? "ok" : "failed",
            Path = NormalizePath(buildPath),
            Configuration = request.Configuration,
            DurationMs = (int)stopwatch.ElapsedMilliseconds,
            ExitCode = process.ExitCode,
            Stdout = limitedStdout.Text,
            StdoutTruncated = limitedStdout.Truncated,
            Stderr = limitedStderr.Text,
            StderrTruncated = limitedStderr.Truncated,
            Diagnostics = diagnostics,
        };
    }

    private static void AddBuildArguments(
        ProcessStartInfo startInfo,
        string buildPath,
        BuildRequest request,
        string artifactsPath)
    {
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(buildPath);
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add(string.IsNullOrWhiteSpace(request.Configuration) ? "Debug" : request.Configuration);
        startInfo.ArgumentList.Add("--verbosity");
        startInfo.ArgumentList.Add("minimal");
        startInfo.ArgumentList.Add("--artifacts-path");
        startInfo.ArgumentList.Add(artifactsPath);
        startInfo.ArgumentList.Add("--disable-build-servers");
        startInfo.ArgumentList.Add("-p:UseSharedCompilation=false");
        startInfo.ArgumentList.Add("/nr:false");
        if (request.TreatWarningsAsErrors)
            startInfo.ArgumentList.Add("-p:TreatWarningsAsErrors=true");
    }

    private string CreateArtifactsPath(string buildPath, string configuration)
    {
        var basePath = Path.Combine(_rootPath, PatchWorkspace.ControlDirectoryName, "patch-build");
        Directory.CreateDirectory(basePath);
        PruneLegacyGuidDirectories(basePath);

        var relativePath = Path.Combine(
            PatchWorkspace.ControlDirectoryName,
            "patch-build",
            StableKey(buildPath, configuration));
        Directory.CreateDirectory(Path.Combine(_rootPath, relativePath));
        return relativePath;
    }

    private string StableKey(string targetPath, string configuration)
    {
        var name = SanitizeFileName(Path.GetFileNameWithoutExtension(targetPath));
        var input = $"{NormalizePath(targetPath)}|{configuration}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))[..12].ToLowerInvariant();
        return $"{name}-{configuration.ToLowerInvariant()}-{hash}";
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) ? '-' : c).ToArray();
        return string.IsNullOrWhiteSpace(value) ? "target" : new string(chars);
    }

    private static (string Text, bool Truncated) LimitOutput(string text)
    {
        if (text.Length <= MaxCapturedOutputChars)
            return (text, false);

        const string marker = "[output truncated]\r\n";
        var keep = Math.Max(0, MaxCapturedOutputChars - marker.Length);
        return (marker + text[^keep..], true);
    }

    private string ResolveBuildPath(string? requestedPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedPath))
        {
            var candidate = Path.GetFullPath(Path.Combine(_rootPath, requestedPath));
            if (!IsUnderRoot(candidate, _rootPath))
                throw new PatchValidationException("path_outside_sandbox", $"Build path is outside the root: {requestedPath}");
            if (!File.Exists(candidate))
                throw new PatchValidationException("file_not_found", $"Build path not found: {requestedPath}");
            return candidate;
        }

        var solution = Directory.EnumerateFiles(_rootPath, "*.slnx", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(_rootPath, "*.sln", SearchOption.TopDirectoryOnly))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (solution is null)
            throw new PatchValidationException("file_not_found", "No .slnx or .sln file found in the active root.");

        return solution;
    }

    private IReadOnlyList<BuildDiagnostic> ParseDiagnostics(string text)
    {
        var diagnostics = new List<BuildDiagnostic>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var match = DiagnosticRegex.Match(line);
            if (!match.Success)
                continue;

            var diagnostic = new BuildDiagnostic
            {
                Kind = match.Groups["kind"].Value.ToLowerInvariant(),
                Code = match.Groups["code"].Value,
                Path = NormalizePath(match.Groups["path"].Value),
                Line = int.Parse(match.Groups["line"].Value),
                Column = int.Parse(match.Groups["column"].Value),
                Message = match.Groups["message"].Value.Trim(),
            };

            var key = string.Join(
                '\u001f',
                diagnostic.Kind,
                diagnostic.Code ?? "",
                diagnostic.Path ?? "",
                diagnostic.Line?.ToString() ?? "",
                diagnostic.Column?.ToString() ?? "",
                diagnostic.Message);
            if (seen.Add(key))
                diagnostics.Add(diagnostic);
        }

        return diagnostics;
    }

    private string NormalizePath(string path)
    {
        var full = Path.GetFullPath(path);
        if (IsUnderRoot(full, _rootPath))
            return Path.GetRelativePath(_rootPath, full).Replace('\\', '/');

        return path.Replace('\\', '/');
    }

    private static bool IsUnderRoot(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullRoot, comparison) ||
               string.Equals(Path.TrimEndingDirectorySeparator(fullPath), Path.TrimEndingDirectorySeparator(fullRoot), comparison);
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Process may have exited between timeout detection and kill.
        }
    }

    private static void PruneLegacyGuidDirectories(string basePath)
    {
        foreach (var directory in Directory.EnumerateDirectories(basePath))
        {
            var name = Path.GetFileName(directory);
            if (name.Length != 32 || !name.All(Uri.IsHexDigit))
                continue;

            TryDeleteDirectory(directory);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Ignored artifacts can be cleaned manually if a process still holds a lock.
        }
    }
}
