using System.Text.Json.Serialization;

namespace ContextMessenger.Core.Roslyn;

public sealed record FindImplementationsResult
{
    [JsonPropertyName("workspaceVersion")]
    public string WorkspaceVersion { get; init; } = "";

    [JsonPropertyName("symbol")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SymbolSummary? Symbol { get; init; }

    [JsonPropertyName("implementations")]
    public IReadOnlyList<SymbolSummary> Implementations { get; init; } = [];
}
