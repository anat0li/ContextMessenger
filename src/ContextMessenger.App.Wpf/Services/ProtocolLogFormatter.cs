using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ContextMessenger.App.Wpf.Settings;

namespace ContextMessenger.App.Wpf.Services;

public sealed class ProtocolLogFormatter : IProtocolLogFormatter
{
    private static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly LoggingSettings _settings;

    public ProtocolLogFormatter(LoggingSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public string FormatRequestBodies(IReadOnlyList<string> requestBodies) =>
        string.Join(
            $"{Environment.NewLine}{Environment.NewLine}",
            requestBodies.Select(FormatJsonIfPossibleWithSettings));

    public string FormatResponse(string responseBlock) =>
        FormatProtocolBlockForLog(responseBlock, "BEGIN_RESPONSE", "END_RESPONSE");

    private string FormatProtocolBlockForLog(string value, string beginMarker, string endMarker)
    {
        var body = value.Trim();
        if (body.StartsWith(beginMarker, StringComparison.OrdinalIgnoreCase))
            body = body[beginMarker.Length..].Trim();
        if (body.EndsWith(endMarker, StringComparison.OrdinalIgnoreCase))
            body = body[..^endMarker.Length].Trim();

        return FormatJsonIfPossibleWithSettings(body);
    }

    private string FormatJsonIfPossibleWithSettings(string value) =>
        FormatJsonIfPossible(value, _settings.MaxJsonPropertyChars);

    private static string FormatJsonIfPossible(string value, int maxJsonPropertyChars)
    {
        try
        {
            var node = JsonNode.Parse(value);
            if (node is null)
                return value.Trim();

            TruncateJsonStrings(node, Math.Max(0, maxJsonPropertyChars));
            return node.ToJsonString(PrettyJsonOptions);
        }
        catch (JsonException)
        {
            return Regex.Replace(value.Trim(), @"\r?\n\s*", Environment.NewLine);
        }
    }

    private static void TruncateJsonStrings(JsonNode node, int maxChars)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var property in obj.ToArray())
                {
                    if (property.Value is JsonValue value &&
                        value.TryGetValue<string>(out var text) &&
                        text.Length > maxChars)
                    {
                        obj[property.Key] = text[..maxChars] + "...";
                    }
                    else if (property.Value is not null)
                    {
                        TruncateJsonStrings(property.Value, maxChars);
                    }
                }
                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    if (item is not null)
                        TruncateJsonStrings(item, maxChars);
                }
                break;
        }
    }
}
