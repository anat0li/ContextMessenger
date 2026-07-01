using System.Text.Json;

namespace ContextMessenger.Protocol.Dispatch;

/// <summary>
/// Pulls build-stage diagnostics out of a serialized patch <c>build</c> payload object. Kept
/// separate from <see cref="CommandDispatcher"/> so the parsing is unit-testable.
/// </summary>
public static class PatchBuildErrorExtractor
{
    public static IReadOnlyList<PatchBuildError> FromBuildElement(JsonElement build, string stage = "build")
    {
        if (build.ValueKind != JsonValueKind.Object ||
            !build.TryGetProperty("diagnostics", out var diagnostics) ||
            diagnostics.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<PatchBuildError>? errors = null;
        foreach (var diagnostic in diagnostics.EnumerateArray())
        {
            if (diagnostic.ValueKind != JsonValueKind.Object)
                continue;
            if (!string.Equals(GetString(diagnostic, "kind"), "error", StringComparison.OrdinalIgnoreCase))
                continue;

            errors ??= [];
            errors.Add(new PatchBuildError
            {
                Code = GetString(diagnostic, "code") ?? stage,
                Path = GetString(diagnostic, "path"),
                Line = GetInt(diagnostic, "line"),
                Column = GetInt(diagnostic, "column"),
                Message = FormatDiagnosticMessage(stage, diagnostic),
            });
        }

        return errors ?? [];
    }

    public static IReadOnlyList<PatchBuildWarning> WarningsFromBuildElement(JsonElement build)
    {
        if (build.ValueKind != JsonValueKind.Object ||
            !build.TryGetProperty("diagnostics", out var diagnostics) ||
            diagnostics.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<PatchBuildWarning>? warnings = null;
        foreach (var diagnostic in diagnostics.EnumerateArray())
        {
            if (diagnostic.ValueKind != JsonValueKind.Object)
                continue;
            if (!string.Equals(GetString(diagnostic, "kind"), "warning", StringComparison.OrdinalIgnoreCase))
                continue;

            warnings ??= [];
            warnings.Add(new PatchBuildWarning
            {
                Code = GetString(diagnostic, "code"),
                Path = GetString(diagnostic, "path"),
                Line = GetInt(diagnostic, "line"),
                Column = GetInt(diagnostic, "column"),
                Message = GetString(diagnostic, "message") ?? "",
            });
        }

        return warnings ?? [];
    }

    private static string? GetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    private static string FormatDiagnosticMessage(string stage, JsonElement diagnostic)
    {
        var message = GetString(diagnostic, "message") ?? "";
        if (!string.IsNullOrWhiteSpace(GetString(diagnostic, "path")))
            return message;

        return string.IsNullOrWhiteSpace(message)
            ? $"{stage} stage failed."
            : $"{stage} stage failed: {message}";
    }
}
