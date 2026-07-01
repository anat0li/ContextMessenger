using System.Text.Json.Serialization;

namespace ContextMessenger.Core.Roslyn;

public sealed record FindDerivedTypesResult
{
    [JsonPropertyName("workspaceVersion")]
    public string WorkspaceVersion { get; init; } = "";

    [JsonPropertyName("symbol")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SymbolSummary? Symbol { get; init; }

    [JsonPropertyName("derivedTypes")]
    public IReadOnlyList<SymbolSummary> DerivedTypes { get; init; } = [];
}
