using System.Text.Json;
using System.Text;
using ContextMessenger.Protocol.Wire;

namespace ContextMessenger.Protocol;

public static class ProtocolParser
{
    public static IReadOnlyList<ContextRequest> ParseBody(string input)
    {
        input = input?.Trim() ?? "";
        if (string.IsNullOrEmpty(input))
            throw new ProtocolException(
                ProtocolErrorCodes.InvalidJson,
                "Body must be a JSON object or array.");
        input = RemoveRenderedLineBreaksInsideStrings(input);

        try
        {
            return input[0] switch
            {
                '{' => ParseObject(input),
                '[' => ParseArray(input),
                _ => throw new ProtocolException(
                    ProtocolErrorCodes.InvalidJson,
                    "Request body must start with a JSON object or array. Remove markdown fences and text outside the JSON body."),
            };
        }
        catch (JsonException ex)
        {
            throw new ProtocolException(
                ProtocolErrorCodes.InvalidJson,
                $"Request JSON is invalid: {ex.Message} If embedding source code in newContent, escape quotes as \\\" or use newContentEncoding: \"base64utf8\" / \"gzipbase64utf8\".",
                ex);
        }
    }

    public static IReadOnlyList<ContextRequest> ParseBodyAndValidate(string input)
    {
        var batch = ParseBody(input);
        ProtocolValidator.Validate(batch);
        return batch;
    }

    private static IReadOnlyList<ContextRequest> ParseObject(string jsonContent)
    {
        var parsed = JsonSerializer.Deserialize<ContextRequest>(jsonContent, JsonOptions.Strict)
                        ?? throw new ProtocolException(ProtocolErrorCodes.InvalidJson,
                                                        "Request JSON deserialized to null.");
        return [parsed];
    }

    private static IReadOnlyList<ContextRequest> ParseArray(string jsonContent)
    {
        return JsonSerializer.Deserialize<List<ContextRequest>>(jsonContent, JsonOptions.Strict)
                    ?? throw new ProtocolException(ProtocolErrorCodes.InvalidJson,
                                                   "Request JSON deserialized to null.");
    }

    private static string RemoveRenderedLineBreaksInsideStrings(string input)
    {
        StringBuilder? builder = null;
        var inString = false;
        var escaped = false;

        for (var i = 0; i < input.Length; i++)
        {
            var ch = input[i];

            if (inString && ch is '\r' or '\n')
            {
                builder ??= new StringBuilder(input.Length).Append(input, 0, i);

                if (ch == '\r' && i + 1 < input.Length && input[i + 1] == '\n')
                    i++;
                while (i + 1 < input.Length && input[i + 1] is ' ' or '\t')
                    i++;

                escaped = false;
                continue;
            }

            builder?.Append(ch);

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (ch == '\\' && inString)
            {
                escaped = true;
                continue;
            }

            if (ch == '"')
                inString = !inString;
        }

        return builder?.ToString() ?? input;
    }
}
