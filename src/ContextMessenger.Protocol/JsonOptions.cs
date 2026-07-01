using System.Text.Json;
using System.Text.Encodings.Web;

namespace ContextMessenger.Protocol;

internal static class JsonOptions
{
    public static JsonSerializerOptions Strict { get; } = new()
    {
        PropertyNameCaseInsensitive = false,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static JsonSerializerOptions Indented { get; } = new(Strict)
    {
        WriteIndented = true,
    };
}
