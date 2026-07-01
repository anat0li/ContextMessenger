using System.Text.Json.Serialization;

namespace ContextMessenger.Core.Roslyn;

public sealed record FindCallersResult
{
    [JsonPropertyName("workspaceVersion")]
    public string WorkspaceVersion { get; init; } = "";

    [JsonPropertyName("symbol")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SymbolSummary? Symbol { get; init; }

    [JsonPropertyName("callers")]
    public IReadOnlyList<ReferenceLocation> Callers { get; init; } = [];
}
