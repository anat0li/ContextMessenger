using System.Text.Json;

namespace ContextMessenger.Protocol.Dispatch;

/// <summary>
/// Pulls failed-test diagnostics out of a serialized patch <c>tests</c> payload object. Only
/// diagnostics with <c>kind == "test"</c> are returned (build errors/warnings are ignored). Kept
/// separate from <see cref="CommandDispatcher"/> so the parsing is unit-testable.
/// </summary>
public static class PatchTestFailureExtractor
{
    public static IReadOnlyList<PatchTestFailure> FromTestsElement(JsonElement tests)
    {
        if (tests.ValueKind != JsonValueKind.Object ||
            !tests.TryGetProperty("diagnostics", out var diagnostics) ||
            diagnostics.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<PatchTestFailure>? failures = null;
        foreach (var diagnostic in diagnostics.EnumerateArray())
        {
            if (diagnostic.ValueKind != JsonValueKind.Object)
                continue;
            if (!string.Equals(GetString(diagnostic, "kind"), "test", StringComparison.OrdinalIgnoreCase))
                continue;

            failures ??= [];
            failures.Add(new PatchTestFailure
            {
                Code = GetString(diagnostic, "code"),
                Path = GetString(diagnostic, "path"),
                Line = GetInt(diagnostic, "line"),
                Column = GetInt(diagnostic, "column"),
                Message = GetString(diagnostic, "message") ?? "",
            });
        }

        return failures ?? [];
    }

    private static string? GetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;
}
