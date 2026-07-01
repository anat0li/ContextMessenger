namespace ContextMessenger.Protocol;

public sealed class RequestTextScanResult
{
    public IReadOnlyList<string> Bodies { get; init; } = [];

    public bool HasMessageAnchor { get; init; }

    public bool HasReadyAnchor { get; init; }

    public int StartAt { get; init; }

    public int ReadyAt { get; init; } = -1;

    public int TextLength { get; init; }

    public bool HasBeginMarker { get; init; }

    public bool HasEndMarker { get; init; }

    public char? FirstNonWhitespaceAfterBeginMarker { get; init; }

    public bool HasInvalidJsonCandidate { get; init; }

    public string? InvalidJsonMessage { get; init; }

    public bool ReturnedInvalidBody { get; init; }
}
