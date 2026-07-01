using ContextMessenger.App.Wpf.Patching;
using ContextMessenger.App.Wpf.ViewModels;

namespace ContextMessenger.App.Wpf.Tests;

public sealed class CommentAnchorResolverTests
{
    [Fact]
    public void Same_line_with_same_text_remains_current()
    {
        var resolver = new CommentAnchorResolver();
        var comment = Comment(line: 2, content: "one\ntwo\nthree\n");

        var changed = resolver.Reanchor([comment], [], _ => "one\ntwo\nthree\n");

        Assert.False(changed);
        Assert.Equal(2, comment.Line);
        Assert.Equal(CommentAnchorStatus.Current, comment.AnchorStatus);
    }

    [Fact]
    public void Inserted_lines_before_anchor_move_comment_down()
    {
        var resolver = new CommentAnchorResolver();
        var comment = Comment(line: 3, content: "one\ntwo\ntarget\nfour\n");

        var changed = resolver.Reanchor([comment], [], _ => "one\ninserted\ntwo\ntarget\nfour\n");

        Assert.True(changed);
        Assert.Equal(4, comment.Line);
        Assert.Equal(CommentAnchorStatus.Moved, comment.AnchorStatus);
    }

    [Fact]
    public void Deleted_lines_before_anchor_move_comment_up()
    {
        var resolver = new CommentAnchorResolver();
        var comment = Comment(line: 4, content: "one\ntwo\nthree\ntarget\n");

        resolver.Reanchor([comment], [], _ => "one\ntarget\n");

        Assert.Equal(2, comment.Line);
        Assert.Equal(CommentAnchorStatus.Moved, comment.AnchorStatus);
    }

    [Fact]
    public void Changed_anchor_text_uses_context_and_flags_changed()
    {
        var resolver = new CommentAnchorResolver();
        var comment = Comment(line: 3, content: "one\nbefore\ntarget\nafter\n");

        resolver.Reanchor([comment], [], _ => "one\nbefore\nrenamed\nafter\n");

        Assert.Equal(3, comment.Line);
        Assert.Equal(CommentAnchorStatus.Changed, comment.AnchorStatus);
    }

    [Fact]
    public void File_outside_modified_list_still_resolves_from_current_content()
    {
        var resolver = new CommentAnchorResolver();
        var comment = Comment(line: 2, content: "one\ntarget\nthree\n");

        resolver.Reanchor([comment], [], _ => "one\ninserted\ntarget\nthree\n");

        Assert.Equal(3, comment.Line);
        Assert.Equal(CommentAnchorStatus.Moved, comment.AnchorStatus);
    }

    [Fact]
    public void Deleted_file_keeps_line_and_marks_deleted()
    {
        var resolver = new CommentAnchorResolver();
        var comment = Comment(line: 2, content: "one\ntarget\n");

        resolver.Reanchor(
            [comment],
            [new PatchReviewFile { Path = "src/File.cs", Operation = "delete" }],
            _ => null);

        Assert.Equal(2, comment.Line);
        Assert.Equal(CommentAnchorStatus.Deleted, comment.AnchorStatus);
    }

    [Fact]
    public void Thread_identifier_mentions_recover_from_stale_physical_line()
    {
        var resolver = new CommentAnchorResolver();
        var comment = new ReviewComment
        {
            Id = "c-1",
            Path = "src/File.cs",
            Line = 107,
            AnchorText = "public void Current_is_null_initially()",
            BeforeContext = ["    [Fact]"],
            AfterContext = ["    {"],
        };
        comment.Messages.Add(new CommentMessage(CommentAuthor.Reviewer, "You", "What's the goal of this testing method?"));
        comment.Messages.Add(new CommentMessage(
            CommentAuthor.Model,
            "ChatGPT",
            "The reviewed method is Save_then_Current_returns_interaction."));

        resolver.Reanchor(
            [comment],
            [],
            _ => string.Join('\n', Enumerable.Range(1, 120).Select(i => i switch
            {
                107 => "    public void Current_is_null_initially()",
                116 => "    public void Save_then_Current_returns_interaction()",
                _ => $"line {i}",
            })));

        Assert.Equal(116, comment.Line);
        Assert.Equal(CommentAnchorStatus.Moved, comment.AnchorStatus);
    }

    [Fact]
    public void Previously_changed_anchor_can_move_even_when_stale_line_still_matches()
    {
        var resolver = new CommentAnchorResolver();
        var comment = new ReviewComment
        {
            Id = "c-1",
            Path = "src/File.cs",
            Line = 107,
            AnchorText = "    public void Current_is_null_initially()",
            AnchorStatus = CommentAnchorStatus.Changed,
            BeforeContext = ["    [Fact]"],
            AfterContext = ["    {"],
        };
        comment.Messages.Add(new CommentMessage(CommentAuthor.Reviewer, "You", "What's the goal of this testing method?"));
        comment.Messages.Add(new CommentMessage(
            CommentAuthor.Model,
            "ChatGPT",
            "The reviewed method is Save_then_Current_returns_interaction."));

        resolver.Reanchor(
            [comment],
            [],
            _ => string.Join('\n', Enumerable.Range(1, 120).Select(i => i switch
            {
                107 => "    public void Current_is_null_initially()",
                116 => "    public void Save_then_Current_returns_interaction()",
                _ => $"line {i}",
            })));

        Assert.Equal(116, comment.Line);
        Assert.Equal(CommentAnchorStatus.Moved, comment.AnchorStatus);
    }

    private static ReviewComment Comment(int line, string content)
    {
        var resolver = new CommentAnchorResolver();
        var anchor = resolver.Capture("src/File.cs", line, content);
        return new ReviewComment
        {
            Id = "c-1",
            Path = "src/File.cs",
            Line = line,
            AnchorText = anchor.AnchorText,
            BeforeContext = anchor.BeforeContext,
            AfterContext = anchor.AfterContext,
        };
    }
}
