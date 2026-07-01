using System.Text.Json.Serialization;

namespace ContextMessenger.Core.Roslyn;

public sealed record DocumentSymbolsResult
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = "";

    [JsonPropertyName("symbols")]
    public IReadOnlyList<DocumentSymbol> Symbols { get; init; } = [];
}
