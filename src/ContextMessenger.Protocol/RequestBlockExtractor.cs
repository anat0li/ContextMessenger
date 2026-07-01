using System.Text;

namespace ContextMessenger.Protocol;

public static class RequestBlockExtractor
{
    public static RequestBlockExtraction Extract(
        string text,
        string beginMarker,
        string endMarker,
        bool repairUnterminatedQuotes = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(beginMarker);
        ArgumentException.ThrowIfNullOrWhiteSpace(endMarker);

        var hasBeginMarker = false;
        var hasEndMarker = false;
        char? firstNonWhitespace = null;
        var validBodies = new List<string>();
        string? lastInvalidJsonBody = null;
        string? invalidJsonMessage = null;

        var inBlock = false;
        var inCodeFence = false;
        var body = new StringBuilder();

        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (!inBlock && IsCodeFenceLine(line))
            {
                inCodeFence = !inCodeFence;
                continue;
            }

            if (inCodeFence)
                continue;

            if (TryReadBeginLine(line, beginMarker, out var beginRemainder))
            {
                hasBeginMarker = true;
                inBlock = true;
                body.Clear();

                if (TryReadEndLine(beginRemainder, endMarker, out var singleLineBody))
                {
                    hasEndMarker = true;
                    AppendBodyFragment(body, singleLineBody, ref firstNonWhitespace);
                    ProcessCandidate(body.ToString().Trim());
                    inBlock = false;
                    body.Clear();
                    continue;
                }

                AppendBodyFragment(body, beginRemainder, ref firstNonWhitespace);
                continue;
            }

            if (TryReadEndLine(line, endMarker, out var endPrefix))
            {
                hasEndMarker = true;
                if (inBlock)
                {
                    AppendBodyFragment(body, endPrefix, ref firstNonWhitespace);
                    ProcessCandidate(body.ToString().Trim());
                }

                inBlock = false;
                body.Clear();
                continue;
            }

            if (inBlock)
                AppendBodyFragment(body, line, ref firstNonWhitespace);
        }

        var returnedInvalidBody = validBodies.Count == 0 && lastInvalidJsonBody is not null;
        return new RequestBlockExtraction
        {
            Bodies = validBodies.Count > 0 ? validBodies : returnedInvalidBody ? [lastInvalidJsonBody!] : [],
            HasBeginMarker = hasBeginMarker,
            HasEndMarker = hasEndMarker,
            FirstNonWhitespaceAfterBeginMarker = firstNonWhitespace,
            HasInvalidJsonCandidate = invalidJsonMessage is not null,
            InvalidJsonMessage = invalidJsonMessage,
            ReturnedInvalidBody = returnedInvalidBody,
        };

        void ProcessCandidate(string candidate)
        {
            var validation = ValidateRequestBody(candidate);

            // Recovery path for chat clients that emit unescaped quotes inside
            // free-text values. Only attempt it when opted in and only on bodies
            // that parsed as JSON candidates but failed validation, so already
            // valid requests are never rewritten.
            if (!validation.IsValid &&
                repairUnterminatedQuotes &&
                validation.IsJsonCandidate &&
                TryRepairUnterminatedQuotes(candidate, out var repaired))
            {
                var revalidation = ValidateRequestBody(repaired);
                if (revalidation.IsValid)
                {
                    candidate = repaired;
                    validation = revalidation;
                }
            }

            if (validation.IsValid)
            {
                validBodies.Add(candidate);
            }
            else if (validation.IsJsonCandidate)
            {
                lastInvalidJsonBody = candidate;
                invalidJsonMessage = validation.ErrorMessage;
            }
        }
    }

    private static bool TryRepairUnterminatedQuotes(string candidate, out string repaired)
    {
        try
        {
            repaired = Json.Lexer.Escape(candidate);
            return true;
        }
        catch (ProtocolException)
        {
            // The repair lexer rejected the body; fall back to the original
            // invalid-candidate handling rather than masking the failure.
            repaired = candidate;
            return false;
        }
    }

    private static bool TryReadBeginLine(string line, string marker, out string remainder)
    {
        remainder = string.Empty;
        var markerStart = 0;
        while (markerStart < line.Length && char.IsWhiteSpace(line[markerStart]))
            markerStart++;

        if (!StartsWithMarker(line, markerStart, marker))
            return false;

        var afterMarker = markerStart + marker.Length;
        if (afterMarker < line.Length && !char.IsWhiteSpace(line[afterMarker]))
            return false;

        remainder = line[afterMarker..];
        return true;
    }

    private static bool TryReadEndLine(string line, string marker, out string prefix)
    {
        prefix = string.Empty;
        var markerEnd = line.Length;
        while (markerEnd > 0 && char.IsWhiteSpace(line[markerEnd - 1]))
            markerEnd--;

        var markerStart = markerEnd - marker.Length;
        if (markerStart < 0 || !StartsWithMarker(line, markerStart, marker))
            return false;

        if (markerStart > 0 && !char.IsWhiteSpace(line[markerStart - 1]))
            return false;

        prefix = line[..markerStart];
        return true;
    }

    private static bool StartsWithMarker(string line, int start, string marker) =>
        start >= 0 &&
        start + marker.Length <= line.Length &&
        string.Compare(line, start, marker, 0, marker.Length, StringComparison.OrdinalIgnoreCase) == 0;

    private static void AppendBodyFragment(StringBuilder body, string fragment, ref char? firstNonWhitespace)
    {
        firstNonWhitespace ??= FindFirstNonWhitespace(fragment);
        if (body.Length > 0)
            body.AppendLine();
        body.Append(fragment);
    }

    private static bool IsCodeFenceLine(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("```", StringComparison.Ordinal) ||
               trimmed.StartsWith("~~~", StringComparison.Ordinal);
    }

    private static RequestBodyValidation ValidateRequestBody(string body)
    {
        if (body.Length == 0)
            return RequestBodyValidation.NotJsonCandidate();

        var first = body[0];
        if (first is not ('{' or '['))
            return RequestBodyValidation.NotJsonCandidate();

        try
        {
            ProtocolParser.ParseBodyAndValidate(body);
            return RequestBodyValidation.Valid();
        }
        catch (ProtocolException ex) when (ex.Code == ProtocolErrorCodes.InvalidJson)
        {
            return RequestBodyValidation.JsonCandidate(ex.Message);
        }
        catch (ProtocolException)
        {
            return RequestBodyValidation.NotJsonCandidate();
        }
    }

    private static char? FindFirstNonWhitespace(string line)
    {
        for (var i = 0; i < line.Length; i++)
        {
            if (!char.IsWhiteSpace(line[i]))
                return line[i];
        }

        return null;
    }

    private readonly record struct RequestBodyValidation(
        bool IsValid,
        bool IsJsonCandidate,
        string? ErrorMessage)
    {
        public static RequestBodyValidation Valid() => new(true, true, null);

        public static RequestBodyValidation NotJsonCandidate() => new(false, false, null);

        public static RequestBodyValidation JsonCandidate(string message) => new(false, true, message);
    }
}
