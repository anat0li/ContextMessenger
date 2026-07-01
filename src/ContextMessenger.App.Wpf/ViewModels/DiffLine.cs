namespace ContextMessenger.App.Wpf.ViewModels;

/// <summary>Classification of a single line in a unified diff, used to color the diff view.</summary>
public enum DiffLineKind
{
    /// <summary>File header lines: <c>diff --git</c>, <c>index</c>, <c>---</c>, <c>+++</c>.</summary>
    Header,

    /// <summary>Hunk range marker: <c>@@ -a,b +c,d @@</c>.</summary>
    Hunk,

    /// <summary>An added line (<c>+</c>).</summary>
    Added,

    /// <summary>A removed line (<c>-</c>).</summary>
    Removed,

    /// <summary>An unchanged context line.</summary>
    Context,
}

/// <summary>
/// One rendered line of a unified diff: its kind, display text without the +/- marker, and the
/// old/new file line numbers when the row maps to source.
/// </summary>
public sealed record DiffLine
{
    public required DiffLineKind Kind { get; init; }

    public required string Text { get; init; }

    public int? OldLineNumber { get; init; }

    public int? NewLineNumber { get; init; }
}
