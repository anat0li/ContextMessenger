using System.Text.Json;
using System.Text.Json.Nodes;
using ContextMessenger.Protocol.Compression;

namespace ContextMessenger.Protocol.Review;

/// <summary>
/// Injects a top-level <c>reviewerComments</c> array into a held <c>BEGIN_RESPONSE … END_RESPONSE</c>
/// block when the human reviewer sends comments to the model. A gzip envelope is decoded and the
/// augmented response re-emitted uncompressed; an unrecognizable block is returned unchanged.
/// </summary>
public static class ReviewResponseAugmenter
{
    public static string Augment(string responseText, IReadOnlyList<ReviewerComment> comments)
    {
        if (string.IsNullOrEmpty(responseText) || comments.Count == 0)
            return responseText;

        if (!TryExtractJson(responseText, out var json))
            return responseText;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return responseText;
        }

        if (root is null || !TryAugment(root, comments, out var augmented))
            return responseText;

        var newJson = augmented.ToJsonString(JsonOptions.Indented);
        return $"{ProtocolDelimiters.BeginResponse}\n{newJson}\n{ProtocolDelimiters.EndResponse}";
    }

    private static bool TryExtractJson(string responseText, out string json)
    {
        json = "";
        var start = responseText.IndexOf(ProtocolDelimiters.BeginResponse, StringComparison.Ordinal);
        var end = responseText.LastIndexOf(ProtocolDelimiters.EndResponse, StringComparison.Ordinal);
        if (start < 0 || end < 0 || end <= start)
            return false;

        var bodyStart = start + ProtocolDelimiters.BeginResponse.Length;
        json = responseText[bodyStart..end].Trim();
        return json.Length > 0;
    }

    // Returns the node to emit (uncompressed). Adds reviewerComments to the response object(s),
    // unwrapping a gzip envelope first.
    private static bool TryAugment(JsonNode root, IReadOnlyList<ReviewerComment> comments, out JsonNode result)
    {
        result = root;

        switch (root)
        {
            case JsonObject obj when IsEnvelope(obj):
                var inner = DecodeEnvelope(obj);
                if (inner is null || !TryAugment(inner, comments, out result))
                    return false;
                return true;

            case JsonObject obj:
                obj["reviewerComments"] = BuildComments(comments);
                result = obj;
                return true;

            case JsonArray array:
                var augmentedAny = false;
                foreach (var item in array)
                {
                    if (item is JsonObject element && !IsEnvelope(element))
                    {
                        element["reviewerComments"] = BuildComments(comments);
                        augmentedAny = true;
                    }
                }
                result = array;
                return augmentedAny;

            default:
                return false;
        }
    }

    private static bool IsEnvelope(JsonObject obj) =>
        obj["payload"] is JsonValue && obj["results"] is null && obj["version"] is null;

    private static JsonNode? DecodeEnvelope(JsonObject envelope)
    {
        if (envelope["payload"]?.GetValue<string>() is not { } payload)
            return null;

        try
        {
            return JsonNode.Parse(GzipBase64.Decode(payload));
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    private static JsonArray BuildComments(IReadOnlyList<ReviewerComment> comments)
    {
        var array = new JsonArray();
        foreach (var comment in comments)
        {
            array.Add(new JsonObject
            {
                ["id"] = comment.Id,
                ["path"] = comment.Path,
                ["line"] = comment.Line,
                ["comment"] = comment.Comment,
                ["openIssue"] = comment.OpenIssue,
            });
        }

        return array;
    }
}
