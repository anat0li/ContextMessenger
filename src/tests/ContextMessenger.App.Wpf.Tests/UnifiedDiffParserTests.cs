using ContextMessenger.App.Wpf.Patching;
using ContextMessenger.App.Wpf.ViewModels;

namespace ContextMessenger.App.Wpf.Tests;

public sealed class UnifiedDiffParserTests
{
    [Fact]
    public void Empty_or_null_input_yields_no_lines()
    {
        Assert.Empty(UnifiedDiffParser.Parse(null));
        Assert.Empty(UnifiedDiffParser.Parse(""));
    }

    [Fact]
    public void Classifies_headers_hunks_and_content_lines()
    {
        const string diff =
            "diff --git a/file.cs b/file.cs\n" +
            "index 111..222 100644\n" +
            "--- a/file.cs\n" +
            "+++ b/file.cs\n" +
            "@@ -1,3 +1,3 @@\n" +
            " context line\n" +
            "-removed line\n" +
            "+added line\n";

        var lines = UnifiedDiffParser.Parse(diff);

        Assert.Equal(DiffLineKind.Header, lines[0].Kind); // diff --git
        Assert.Equal(DiffLineKind.Header, lines[1].Kind); // index
        Assert.Equal(DiffLineKind.Header, lines[2].Kind); // ---
        Assert.Equal(DiffLineKind.Header, lines[3].Kind); // +++
        Assert.Equal(DiffLineKind.Hunk, lines[4].Kind);   // @@
        Assert.Equal(DiffLineKind.Context, lines[5].Kind);
        Assert.Equal(DiffLineKind.Removed, lines[6].Kind);
        Assert.Equal(DiffLineKind.Added, lines[7].Kind);
    }

    [Fact]
    public void Strips_marker_from_content_lines_but_keeps_headers_verbatim()
    {
        const string diff =
            "@@ -1 +1 @@\n" +
            " ctx\n" +
            "-old\n" +
            "+new\n";

        var lines = UnifiedDiffParser.Parse(diff);

        Assert.Equal("@@ -1 +1 @@", lines[0].Text); // hunk kept whole
        Assert.Equal("ctx", lines[1].Text);          // leading space stripped
        Assert.Equal("old", lines[2].Text);          // '-' stripped
        Assert.Equal("new", lines[3].Text);          // '+' stripped
    }

    [Fact]
    public void Triple_dash_and_plus_markers_are_headers_not_content()
    {
        var lines = UnifiedDiffParser.Parse("--- a/x\n+++ b/x\n");

        Assert.All(lines, l => Assert.Equal(DiffLineKind.Header, l.Kind));
    }

    [Fact]
    public void Parse_assigns_old_and_new_line_numbers_from_hunk_headers()
    {
        var lines = UnifiedDiffParser.Parse(
            """
            @@ -105,4 +105,7 @@
             before
            -old
            +inserted 1
            +inserted 2
            +inserted 3
             target
            """);

        var content = lines.Where(line => line.Kind is not DiffLineKind.Hunk).ToArray();

        Assert.Equal(105, content[0].OldLineNumber);
        Assert.Equal(105, content[0].NewLineNumber);
        Assert.Equal(106, content[1].OldLineNumber);
        Assert.Null(content[1].NewLineNumber);
        Assert.Null(content[2].OldLineNumber);
        Assert.Equal(106, content[2].NewLineNumber);
        Assert.Null(content[3].OldLineNumber);
        Assert.Equal(107, content[3].NewLineNumber);
        Assert.Null(content[4].OldLineNumber);
        Assert.Equal(108, content[4].NewLineNumber);
        Assert.Equal(107, content[5].OldLineNumber);
        Assert.Equal(109, content[5].NewLineNumber);
    }

    [Fact]
    public void Parse_resets_line_numbers_for_each_hunk()
    {
        var lines = UnifiedDiffParser.Parse(
            """
            @@ -10,1 +20,1 @@
             first
            @@ -30,1 +40,1 @@
             second
            """);

        var content = lines.Where(line => line.Kind == DiffLineKind.Context).ToArray();

        Assert.Equal(10, content[0].OldLineNumber);
        Assert.Equal(20, content[0].NewLineNumber);
        Assert.Equal(30, content[1].OldLineNumber);
        Assert.Equal(40, content[1].NewLineNumber);
    }
}
