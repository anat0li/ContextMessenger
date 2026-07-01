using System.Text.Json.Serialization;

namespace ContextMessenger.Core.Roslyn;

public sealed class SymbolSourceBlock
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = "";

    [JsonPropertyName("startLine")]
    public int StartLine { get; init; }

    [JsonPropertyName("startColumn")]
    public int StartColumn { get; init; } = 1;

    [JsonPropertyName("endLine")]
    public int EndLine { get; init; }

    [JsonPropertyName("endColumn")]
    public int EndColumn { get; init; }

    [JsonPropertyName("language")]
    public string Language { get; init; } = "csharp";

    [JsonPropertyName("text")]
    public string Text { get; init; } = "";

    [JsonPropertyName("hash")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Hash { get; init; }

    [JsonPropertyName("oldSourceHash")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OldSourceHash { get; init; }
}
