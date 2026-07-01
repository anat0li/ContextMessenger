using System.Text.Json;

namespace ContextMessenger.Protocol.Dispatch;

/// <summary>Compact status/count projection of a patch build or test stage.</summary>
public sealed record PatchStageSummary
{
    public string Status { get; init; } = "";

    public string? Policy { get; init; }

    public string? Path { get; init; }

    public int? DurationMs { get; init; }

    public int? ExitCode { get; init; }

    public int? TotalTests { get; init; }

    public int? ExecutedTests { get; init; }

    public int? PassedTests { get; init; }

    public int? FailedTests { get; init; }

    public int? SkippedTests { get; init; }

    public static PatchStageSummary Empty { get; } = new();

    public static PatchStageSummary FromStageElement(JsonElement stage)
    {
        if (stage.ValueKind != JsonValueKind.Object)
            return Empty;

        return new PatchStageSummary
        {
            Status = GetString(stage, "status") ?? "",
            Policy = GetString(stage, "policy"),
            Path = GetString(stage, "path"),
            DurationMs = GetInt(stage, "durationMs"),
            ExitCode = GetInt(stage, "exitCode"),
            TotalTests = GetInt(stage, "totalTests"),
            ExecutedTests = GetInt(stage, "executedTests"),
            PassedTests = GetInt(stage, "passedTests"),
            FailedTests = GetInt(stage, "failedTests"),
            SkippedTests = GetInt(stage, "skippedTests"),
        };
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
