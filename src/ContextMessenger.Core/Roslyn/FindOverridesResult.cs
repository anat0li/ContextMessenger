using System.Text.Json.Serialization;

namespace ContextMessenger.Core.Roslyn;

public sealed record FindOverridesResult
{
    [JsonPropertyName("workspaceVersion")]
    public string WorkspaceVersion { get; init; } = "";

    [JsonPropertyName("symbol")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SymbolSummary? Symbol { get; init; }

    [JsonPropertyName("overrides")]
    public IReadOnlyList<SymbolSummary> Overrides { get; init; } = [];
}
