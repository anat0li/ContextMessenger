using System.Text.Json.Serialization;

namespace ContextMessenger.Core.Roslyn;

public sealed record DocumentSymbol
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "";

    [JsonPropertyName("line")]
    public int Line { get; init; }

    [JsonPropertyName("endLine")]
    public int EndLine { get; init; }

    [JsonPropertyName("signature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Signature { get; init; }

    [JsonPropertyName("children")]
    public IReadOnlyList<DocumentSymbol> Children { get; init; } = [];
}
