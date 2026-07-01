namespace ContextMessenger.Protocol;

public sealed class RequestBlockExtraction
{
    public IReadOnlyList<string> Bodies { get; init; } = [];

    public bool HasBeginMarker { get; init; }

    public bool HasEndMarker { get; init; }

    public char? FirstNonWhitespaceAfterBeginMarker { get; init; }

    public bool HasInvalidJsonCandidate { get; init; }

    public string? InvalidJsonMessage { get; init; }

    public bool ReturnedInvalidBody { get; init; }
}
