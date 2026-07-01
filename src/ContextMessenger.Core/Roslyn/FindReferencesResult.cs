using System.Text.Json.Serialization;

namespace ContextMessenger.Core.Roslyn;

public sealed record FindReferencesResult
{
    [JsonPropertyName("workspaceVersion")]
    public string WorkspaceVersion { get; init; } = "";

    [JsonPropertyName("symbol")]
    public SymbolSummary? Symbol { get; init; }

    [JsonPropertyName("references")]
    public IReadOnlyList<ReferenceLocation> References { get; init; } = [];
}
