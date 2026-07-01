namespace ContextMessenger.Core.Patching;

public sealed class PatchValidationException : Exception
{
    public PatchValidationException(
        string code,
        string message,
        string? path = null,
        int? editIndex = null,
        string? kind = null,
        int? matchCount = null,
        string? hashField = null,
        string? expectedHash = null,
        string? actualHash = null,
        string? hashTarget = null,
        string? expectedFormat = null,
        string? lineEndingHint = null,
        IReadOnlyList<PatchEditMatchLocation>? matches = null)
        : base(message)
    {
        Code = code;
        Path = path;
        EditIndex = editIndex;
        Kind = kind;
        MatchCount = matchCount;
        HashField = hashField;
        ExpectedHash = expectedHash;
        ActualHash = actualHash;
        HashTarget = hashTarget;
        ExpectedFormat = expectedFormat;
        LineEndingHint = lineEndingHint;
        Matches = matches;
    }

    public string Code { get; }

    public string? Path { get; }

    public int? EditIndex { get; }

    public string? Kind { get; }

    public int? MatchCount { get; }

    public string? HashField { get; }

    public string? ExpectedHash { get; }

    public string? ActualHash { get; }

    public string? HashTarget { get; }

    public string? ExpectedFormat { get; }

    public string? LineEndingHint { get; }

    public IReadOnlyList<PatchEditMatchLocation>? Matches { get; }
}

public sealed record PatchEditMatchLocation(int Line, int Column);
