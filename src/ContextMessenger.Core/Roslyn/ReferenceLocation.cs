using System.Text.Json.Serialization;

namespace ContextMessenger.Core.Roslyn;

public sealed record ReferenceLocation
{
    [JsonPropertyName("symbolId")]
    public string SymbolId { get; init; } = "";

    [JsonPropertyName("project")]
    public string ProjectName { get; init; } = "";

    [JsonPropertyName("path")]
    public string Path { get; init; } = "";

    [JsonPropertyName("line")]
    public int Line { get; init; }

    [JsonPropertyName("column")]
    public int Column { get; init; }

    [JsonPropertyName("text")]
    public string LineText { get; init; } = "";

    [JsonPropertyName("isDefinition")]
    public bool IsDefinition { get; init; }

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "other";
}
