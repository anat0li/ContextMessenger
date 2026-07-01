using ContextMessenger.Protocol;

namespace ContextMessenger.Protocol.Tests;

public sealed class RequestTextScannerTests
{
    [Fact]
    public void Scan_extracts_request_between_last_message_anchor_and_ready_anchor()
    {
        var text = """
            Older Copy message
            BEGIN_REQUEST
            {"version":"1.0","id":"old","commands":[{"type":"tree"}]}
            END_REQUEST
            Copy message
            BEGIN_REQUEST
            {"version":"1.0","id":"new","commands":[{"type":"tree"}]}
            END_REQUEST
            Copy response
            Ready
            """;

        var result = RequestTextScanner.Scan(
            text,
            "Copy message",
            "Copy response",
            "Ready",
            "BEGIN_REQUEST",
            "END_REQUEST");

        var body = Assert.Single(result.Bodies);
        Assert.Contains("\"id\":\"new\"", body);
        Assert.DoesNotContain("\"id\":\"old\"", body);
        Assert.True(result.HasMessageAnchor);
        Assert.True(result.HasReadyAnchor);
    }

    [Fact]
    public void Scan_uses_latest_message_anchor_across_alternatives()
    {
        var text = """
            Copy message
            BEGIN_REQUEST
            {"version":"1.0","id":"old","commands":[{"type":"tree"}]}
            END_REQUEST
            Pasted text.txt
            Document
            BEGIN_REQUEST
            {"version":"1.0","id":"new","commands":[{"type":"tree"}]}
            END_REQUEST
            Copy response
            Ready
            """;

        var result = RequestTextScanner.Scan(
            text,
            ["Copy message", "Pasted text.txt\nDocument"],
            ["Copy response"],
            ["Ready"],
            -1,
            "BEGIN_REQUEST",
            "END_REQUEST");

        var body = Assert.Single(result.Bodies);
        Assert.Contains("\"id\":\"new\"", body);
        Assert.DoesNotContain("\"id\":\"old\"", body);
        Assert.True(result.HasMessageAnchor);
    }

    [Fact]
    public void Scan_uses_ready_anchor_alternatives()
    {
        var text = """
            Copy message
            BEGIN_REQUEST
            {"version":"1.0","id":"x","commands":[{"type":"tree"}]}
            END_REQUEST
            Copy response
            Write a message…
            """;

        var result = RequestTextScanner.Scan(
            text,
            ["Copy message"],
            ["Copy response"],
            ["Add files and more\nAsk anything", "Write a message…"],
            -1,
            "BEGIN_REQUEST",
            "END_REQUEST");

        Assert.Single(result.Bodies);
        Assert.True(result.HasReadyAnchor);
    }

    [Fact]
    public void Scan_returns_all_request_blocks_between_message_and_ready_anchors()
    {
        var text = """
            Copy message
            BEGIN_REQUEST
            {"version":"1.0","id":"a","commands":[{"type":"current_context"}]}
            END_REQUEST
            BEGIN_REQUEST
            {"version":"1.0","id":"b","commands":[{"type":"git_status"}]}
            END_REQUEST
            BEGIN_REQUEST
            {"version":"1.0","id":"c","commands":[{"type":"capabilities"}]}
            END_REQUEST
            Copy response
            Ready
            """;

        var result = RequestTextScanner.Scan(
            text,
            "Copy message",
            "Copy response",
            "Ready",
            "BEGIN_REQUEST",
            "END_REQUEST");

        Assert.Equal(3, result.Bodies.Count);
        Assert.Contains("\"id\":\"a\"", result.Bodies[0]);
        Assert.Contains("\"id\":\"b\"", result.Bodies[1]);
        Assert.Contains("\"id\":\"c\"", result.Bodies[2]);
    }

    [Fact]
    public void Scan_returns_four_pretty_printed_request_blocks()
    {
        var text = """
            Copy message
            BEGIN_REQUEST
            {
            "version": "1.0",
            "id": "6f80c86d-cb1f-4e95-93c4-e9f7ba28b5c2",
            "commands": [
            {
            "type": "current_context"
            }
            ]
            }
            END_REQUEST
            BEGIN_REQUEST
            {
            "version": "1.0",
            "id": "3c813241-0b62-4fbd-945d-ef6e09f4d0df",
            "commands": [
            {
            "type": "git_status"
            }
            ]
            }
            END_REQUEST
            BEGIN_REQUEST
            {
            "version": "1.0",
            "id": "f86e1bda-99a1-4f16-a812-b5aad46adf3b9",
            "commands": [
            {
            "type": "capabilities",
            "command": "get_symbol_source"
            }
            ]
            }
            END_REQUEST
            BEGIN_REQUEST
            {
            "version": "1.0",
            "id": "c0b369fa-5a53-4dd1-8991-3157a327877e",
            "commands": [
            {
            "type": "tree",
            "path": "src/ContextMessenger.Protocol",
            "depth": 2,
            "include": [
            "**/*.cs"
            ]
            }
            ]
            }
            END_REQUEST
            Copy response
            Ready
            """;

        var result = RequestTextScanner.Scan(
            text,
            "Copy message",
            "Copy response",
            "Ready",
            "BEGIN_REQUEST",
            "END_REQUEST");

        Assert.Equal(4, result.Bodies.Count);
        Assert.Contains("6f80c86d-cb1f-4e95-93c4-e9f7ba28b5c2", result.Bodies[0]);
        Assert.Contains("3c813241-0b62-4fbd-945d-ef6e09f4d0df", result.Bodies[1]);
        Assert.Contains("f86e1bda-99a1-4f16-a812-b5aad46adf3b9", result.Bodies[2]);
        Assert.Contains("c0b369fa-5a53-4dd1-8991-3157a327877e", result.Bodies[3]);
    }

    [Fact]
    public void Scan_uses_document_start_when_message_anchor_is_missing()
    {
        var text = """
            BEGIN_REQUEST
            [{"version":"1.0","id":"x","commands":[{"type":"tree"}]}]
            END_REQUEST
            Copy response
            Ready
            """;

        var result = RequestTextScanner.Scan(
            text,
            "Copy message",
            "Copy response",
            "Ready",
            "BEGIN_REQUEST",
            "END_REQUEST");

        Assert.Single(result.Bodies);
        Assert.False(result.HasMessageAnchor);
        Assert.True(result.HasReadyAnchor);
        Assert.Equal(0, result.StartAt);
    }

    [Fact]
    public void Scan_returns_empty_when_ready_anchor_is_missing()
    {
        var text = """
            Copy message
            BEGIN_REQUEST
            {"id":"x","commands":[{"type":"tree"}]}
            END_REQUEST
            Copy response
            """;

        var result = RequestTextScanner.Scan(
            text,
            "Copy message",
            "Copy response",
            "Ready",
            "BEGIN_REQUEST",
            "END_REQUEST");

        Assert.Empty(result.Bodies);
        Assert.True(result.HasMessageAnchor);
        Assert.False(result.HasReadyAnchor);
        Assert.Equal(-1, result.ReadyAt);
    }

    [Fact]
    public void Scan_reports_delimiter_diagnostics_when_json_body_is_not_matched()
    {
        var text = "Copy message BEGIN_REQUEST prose END_REQUEST Copy response Ready";

        var result = RequestTextScanner.Scan(
            text,
            "Copy message",
            "Copy response",
            "Ready",
            "BEGIN_REQUEST",
            "END_REQUEST");

        Assert.Empty(result.Bodies);
        Assert.True(result.HasBeginMarker);
        Assert.True(result.HasEndMarker);
        Assert.Equal('p', result.FirstNonWhitespaceAfterBeginMarker);
    }

    [Fact]
    public void Scan_matches_ready_anchor_when_uia_removes_anchor_newlines()
    {
        var text = """
            Copy message
            BEGIN_REQUEST
            {"version":"1.0","id":"x","commands":[{"type":"tree"}]}
            END_REQUEST
            Copy response
            Add files and moreAsk anythingAuto
            """;

        var result = RequestTextScanner.Scan(
            text,
            "Copy message",
            "Copy response",
            "Add files and more\nAsk anything",
            "BEGIN_REQUEST",
            "END_REQUEST");

        Assert.Single(result.Bodies);
        Assert.True(result.HasReadyAnchor);
    }
}
