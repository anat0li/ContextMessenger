using System.Text.Json.Serialization;

namespace ContextMessenger.Core.Roslyn;

public sealed record SymbolSummary
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "";

    [JsonPropertyName("symbolId")]
    public string SymbolId { get; init; } = "";

    [JsonPropertyName("project")]
    public string ProjectName { get; init; } = "";

    [JsonPropertyName("path")]
    public string Path { get; init; } = "";

    [JsonPropertyName("line")]
    public int Line { get; init; }

    [JsonPropertyName("signature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Signature { get; init; }

    [JsonPropertyName("namespace")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Namespace { get; init; }

    [JsonPropertyName("containingType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ContainingType { get; init; }

    [JsonPropertyName("accessibility")]
    public string Accessibility { get; init; } = "";
}
