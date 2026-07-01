using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContextMessenger.App.Wpf.Settings;

public sealed record TargetAutomationSettings
{
    public string RequestBeginMarker { get; init; } = "BEGIN_REQUEST";

    public string RequestEndMarker { get; init; } = "END_REQUEST";

    public string RootAutomationId { get; init; } = "RootWebArea";

    public AnchorTextSet MessageAnchorText { get; init; } = "Copy message\nEdit message";

    public AnchorTextSet ResponseAnchorText { get; init; } = "Copy response\nGood response";

    public AnchorTextSet ReadyAnchorText { get; init; } = "Add files and more\nAsk anything";

    public int AnchorIgnoreIndex { get; init; } = -1;

    public string InputEditName { get; init; } = "Chat with ChatGPT";

    public string SendButtonName { get; init; } = "Send prompt";

    /// <summary>
    /// When true, request bodies that fail to parse are retried through a
    /// quote-repair pass that folds unescaped quotes inside known free-text
    /// fields into the string value. Opt-in per target; valid requests are
    /// never rewritten.
    /// </summary>
    public bool RepairUnterminatedQuotes { get; init; }
}

[JsonConverter(typeof(AnchorTextSetJsonConverter))]
public readonly struct AnchorTextSet : IEquatable<AnchorTextSet>
{
    private readonly string[]? _values;

    public AnchorTextSet(IEnumerable<string?> values)
    {
        _values = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();
    }

    public IReadOnlyList<string> Values => _values ?? [];

    public static implicit operator AnchorTextSet(string? value) =>
        string.IsNullOrWhiteSpace(value) ? new AnchorTextSet([]) : new AnchorTextSet([value]);

    public bool Equals(AnchorTextSet other) =>
        Values.SequenceEqual(other.Values, StringComparer.Ordinal);

    public override bool Equals(object? obj) =>
        obj is AnchorTextSet other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var value in Values)
            hash.Add(value, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    public override string ToString() =>
        string.Join(" | ", Values);
}

public sealed class AnchorTextSetJsonConverter : JsonConverter<AnchorTextSet>
{
    public override AnchorTextSet Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return reader.GetString();

        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Anchor text must be a string or an array of strings.");

        var values = new List<string?>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                return new AnchorTextSet(values);

            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException("Anchor text arrays may contain only strings.");

            values.Add(reader.GetString());
        }

        throw new JsonException("Anchor text array was not terminated.");
    }

    public override void Write(Utf8JsonWriter writer, AnchorTextSet value, JsonSerializerOptions options)
    {
        if (value.Values.Count == 1)
        {
            writer.WriteStringValue(value.Values[0]);
            return;
        }

        writer.WriteStartArray();
        foreach (var item in value.Values)
            writer.WriteStringValue(item);
        writer.WriteEndArray();
    }
}
