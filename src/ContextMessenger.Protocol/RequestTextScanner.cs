namespace ContextMessenger.Protocol;

public static class RequestTextScanner
{
    public static RequestTextScanResult Scan(
        string text,
        string messageAnchor,
        string responseAnchors,
        string readyAnchor,
        string beginMarker,
        string endMarker,
        int anchorIgnoreIndex = -1,
        bool repairUnterminatedQuotes = false) =>
        Scan(
            text,
            string.IsNullOrEmpty(messageAnchor) ? [] : [messageAnchor],
            string.IsNullOrEmpty(responseAnchors) ? [] : [responseAnchors],
            string.IsNullOrEmpty(readyAnchor) ? [] : [readyAnchor],
            anchorIgnoreIndex,
            beginMarker,
            endMarker,
            repairUnterminatedQuotes);

    public static RequestTextScanResult Scan(
        string text,
        IReadOnlyList<string> messageAnchors,
        IReadOnlyList<string> responseAnchors,
        IReadOnlyList<string> readyAnchors,
        int anchorIgnoreIndex,
        string beginMarker,
        string endMarker,
        bool repairUnterminatedQuotes = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(messageAnchors);
        ArgumentNullException.ThrowIfNull(responseAnchors);
        ArgumentNullException.ThrowIfNull(readyAnchors);
        if (readyAnchors.Count == 0 || readyAnchors.All(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one ready anchor is required.", nameof(readyAnchors));
        ArgumentException.ThrowIfNullOrWhiteSpace(beginMarker);
        ArgumentException.ThrowIfNullOrWhiteSpace(endMarker);

        var messageAnchorMatch = messageAnchors.Count == 0
            ? null
            : FindLastAnchor(text, messageAnchors, 0);
        var hasMessageAnchor = messageAnchorMatch is not null;
        var startAt = hasMessageAnchor
            ? messageAnchorMatch!.Value.End
            : 0;
        var readyAnchorMatch = FindFirstAnchor(text, readyAnchors, startAt);
        var readyAt = readyAnchorMatch?.Start ?? -1;
        var responseAt = responseAnchors.Count == 0
            ? readyAt
            : FindLastAnchor(text, responseAnchors, startAt)?.Start ?? -1;
        if (readyAt < 0 || responseAt < 0 || responseAt > readyAt || anchorIgnoreIndex == messageAnchorMatch?.AnchorIndex)
        {
            return new RequestTextScanResult
            {
                HasMessageAnchor = hasMessageAnchor,
                HasReadyAnchor = false,
                StartAt = startAt,
                ReadyAt = readyAt,
                TextLength = text.Length,
            };
        }

        var scanText = text[startAt..responseAt];
        var extraction = RequestBlockExtractor.Extract(scanText, beginMarker, endMarker, repairUnterminatedQuotes);

        return new RequestTextScanResult
        {
            Bodies = extraction.Bodies,
            HasMessageAnchor = hasMessageAnchor,
            HasReadyAnchor = true,
            StartAt = startAt,
            ReadyAt = readyAt,
            TextLength = text.Length,
            HasBeginMarker = extraction.HasBeginMarker,
            HasEndMarker = extraction.HasEndMarker,
            FirstNonWhitespaceAfterBeginMarker = extraction.FirstNonWhitespaceAfterBeginMarker,
            HasInvalidJsonCandidate = extraction.HasInvalidJsonCandidate,
            InvalidJsonMessage = extraction.InvalidJsonMessage,
            ReturnedInvalidBody = extraction.ReturnedInvalidBody,
        };
    }

    private static AnchorMatch? FindFirstAnchor(string text, IReadOnlyList<string> anchors, int startAt)
    {
        AnchorMatch? first = null;
        for (int j = 0; j < anchors.Count; j++)
        {
            var anchor = anchors[j];
            if (string.IsNullOrEmpty(anchor))
                continue;
            for (var i = Math.Max(0, startAt); i < text.Length; i++)
            {
                var end = MatchAnchorAt(text, anchor, i);
                if (end is null)
                    continue;

                var match = new AnchorMatch(i, end.Value, j);
                if (first is null ||
                    match.Start < first.Value.Start ||
                    (match.Start == first.Value.Start && match.End > first.Value.End))
                {
                    first = match;
                }
                break;
            }
        }

        return first;
    }

    private static AnchorMatch? FindLastAnchor(string text, IReadOnlyList<string> anchors, int startAt)
    {
        AnchorMatch? last = null;
        for (int j = 0; j < anchors.Count; j++)
        {
            var anchor = anchors[j];
            if (string.IsNullOrEmpty(anchor))
                continue;
            for (var i = Math.Max(0, startAt); i < text.Length; i++)
            {
                var end = MatchAnchorAt(text, anchor, i);
                if (end is null)
                    continue;

                var match = new AnchorMatch(i, end.Value, j);
                if (last is null ||
                    match.Start > last.Value.Start ||
                    (match.Start == last.Value.Start && match.End > last.Value.End))
                {
                    last = match;
                }
            }
        }

        return last;
    }

    private static int? MatchAnchorAt(string text, string anchor, int textIndex)
    {
        var ti = textIndex;
        var ai = 0;
        while (ai < anchor.Length)
        {
            if (char.IsWhiteSpace(anchor[ai]))
            {
                while (ai < anchor.Length && char.IsWhiteSpace(anchor[ai]))
                    ai++;
                while (ti < text.Length && char.IsWhiteSpace(text[ti]))
                    ti++;
                continue;
            }

            if (ti >= text.Length || text[ti] != anchor[ai])
                return null;

            ti++;
            ai++;
        }

        return ti;
    }

    private readonly record struct AnchorMatch(int Start, int End, int AnchorIndex);
}
