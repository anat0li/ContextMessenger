using System.Text.Json;
using ContextMessenger.Protocol.Wire;

namespace ContextMessenger.Protocol.Dispatch;

/// <summary>
/// Reads the model's <c>commentReplies</c> out of an <c>amend_patch</c> command's parameters.
/// Known ids reply to existing review threads; unknown ids can open model-originated threads.
/// </summary>
public static class PatchCommentReplyExtractor
{
    public static IReadOnlyList<PatchCommentReply> FromCommand(ContextCommand command)
    {
        if (!command.Parameters.TryGetValue("commentReplies", out var element) ||
            element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<PatchCommentReply>? replies = null;
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var id = GetString(item, "id");
            if (string.IsNullOrEmpty(id))
                continue;

            replies ??= [];
            replies.Add(new PatchCommentReply
            {
                Id = id,
                Reply = GetString(item, "reply") ?? "",
                Path = GetString(item, "path") ?? "",
                Line = GetInt(item, "line"),
                OpenIssue = GetBool(item, "openIssue"),
            });
        }

        return replies ?? [];
    }

    private static string? GetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int GetInt(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var line)
            ? line
            : 0;

    private static bool? GetBool(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
