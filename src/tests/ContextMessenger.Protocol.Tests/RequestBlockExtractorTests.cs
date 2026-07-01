using ContextMessenger.Protocol;

namespace ContextMessenger.Protocol.Tests;

public sealed class RequestBlockExtractorTests
{
    [Fact]
    public void Extract_rejects_compact_object_body()
    {
        var text = """BEGIN_REQUEST{"id":"x","commands":[{"type":"tree"}]}END_REQUEST""";

        var result = RequestBlockExtractor.Extract(text, "BEGIN_REQUEST", "END_REQUEST");

        Assert.Empty(result.Bodies);
        Assert.False(result.HasBeginMarker);
        Assert.False(result.HasEndMarker);
    }

    [Fact]
    public void Extract_accepts_array_body()
    {
        var text = """
            BEGIN_REQUEST
            [{"version":"1.0","id":"x","commands":[{"type":"tree"}]}]
            END_REQUEST
            """;

        var result = RequestBlockExtractor.Extract(text, "BEGIN_REQUEST", "END_REQUEST");

        var body = Assert.Single(result.Bodies);
        Assert.StartsWith("[", body);
    }

    [Fact]
    public void Extract_accepts_body_between_begin_at_line_start_and_end_at_line_end()
    {
        var text = """BEGIN_REQUEST [ { "version": "1.0", "id": "6f708192-a3b4-4dc5-6234-bbccddeeff00", "commands": [ { "type": "capabilities" } ] }, { "version": "1.0", "id": "708192a3-b4c5-4ed6-7345-ccddeeff0011", "commands": [ { "type": "list_files", "path": "src/ContextMessenger.Protocol/Commands", "include": ["/*.cs"] } ] }, { "version": "1.0", "id": "8192a3b4-c5d6-4fe7-8456-ddeeff001122", "commands": [ { "type": "list_files", "path": "src/ContextMessenger.Patching", "include": ["/*.cs"] } ] } ] END_REQUEST""";

        var result = RequestBlockExtractor.Extract(text, "BEGIN_REQUEST", "END_REQUEST");

        var body = Assert.Single(result.Bodies);
        Assert.StartsWith("[", body);
        Assert.Contains("\"type\": \"capabilities\"", body);
        Assert.True(result.HasBeginMarker);
        Assert.True(result.HasEndMarker);
        Assert.Equal('[', result.FirstNonWhitespaceAfterBeginMarker);
    }

    [Fact]
    public void Extract_ignores_inline_marker_followed_by_prose()
    {
        var text = """
            The client should emit a BEGIN_REQUEST and END_REQUEST block.
            BEGIN_REQUEST
            {"version":"1.0","id":"x","commands":[{"type":"tree"}]}
            END_REQUEST
            """;

        var result = RequestBlockExtractor.Extract(text, "BEGIN_REQUEST", "END_REQUEST");

        var body = Assert.Single(result.Bodies);
        Assert.StartsWith("{", body);
        Assert.Contains("\"commands\"", body);
    }

    [Fact]
    public void Extract_ignores_request_examples_inside_chat_prompt_markdown_fences()
    {
        var prompt = File.ReadAllText(FindRepoFile("docs/chat-prompt.md"));

        var result = RequestBlockExtractor.Extract(prompt, "BEGIN_REQUEST", "END_REQUEST");

        Assert.Empty(result.Bodies);
        Assert.False(result.HasBeginMarker);
        Assert.False(result.HasEndMarker);
    }

    [Fact]
    public void Extract_ignores_request_examples_inside_markdown_fences()
    {
        var text = """
            ```text
            BEGIN_REQUEST
            {"version":"1.0","id":"x","commands":[{"type":"tree"}]}
            END_REQUEST
            ```
            """;

        var result = RequestBlockExtractor.Extract(text, "BEGIN_REQUEST", "END_REQUEST");

        Assert.Empty(result.Bodies);
        Assert.False(result.HasBeginMarker);
        Assert.False(result.HasEndMarker);
    }

    [Fact]
    public void Extract_ignores_boundary_markers_when_body_is_not_json()
    {
        var text = "BEGIN_REQUEST prose before JSON END_REQUEST";

        var result = RequestBlockExtractor.Extract(text, "BEGIN_REQUEST", "END_REQUEST");

        Assert.Empty(result.Bodies);
        Assert.True(result.HasBeginMarker);
        Assert.True(result.HasEndMarker);
        Assert.Equal('p', result.FirstNonWhitespaceAfterBeginMarker);
    }

    [Fact]
    public void Extract_ignores_malformed_block_before_valid_json_block()
    {
        var text = """
            BEGIN_REQUEST this is prose, not JSON END_REQUEST
            BEGIN_REQUEST
            {"version":"1.0","id":"x","commands":[{"type":"tree"}]}
            END_REQUEST
            """;

        var result = RequestBlockExtractor.Extract(text, "BEGIN_REQUEST", "END_REQUEST");

        var body = Assert.Single(result.Bodies);
        Assert.StartsWith("{", body);
        Assert.Contains("\"id\":\"x\"", body);
    }

    [Fact]
    public void Extract_returns_all_valid_json_blocks_in_order()
    {
        var text = """
            BEGIN_REQUEST
            {"version":"1.0","id":"a","commands":[{"type":"tree"}]}
            END_REQUEST
            BEGIN_REQUEST
            [{"version":"1.0","id":"b","commands":[{"type":"list_files"}]}]
            END_REQUEST
            """;

        var result = RequestBlockExtractor.Extract(text, "BEGIN_REQUEST", "END_REQUEST");

        Assert.Equal(2, result.Bodies.Count);
        Assert.StartsWith("{", result.Bodies[0]);
        Assert.Contains("\"id\":\"a\"", result.Bodies[0]);
        Assert.StartsWith("[", result.Bodies[1]);
        Assert.Contains("\"id\":\"b\"", result.Bodies[1]);
    }

    [Fact]
    public void Extract_returns_four_contiguous_request_blocks()
    {
        var text = """
            BEGIN_REQUEST
            {"version":"1.0","id":"a0a25c8c-0979-47da-bf0e-4a835edb9b25","commands":[{"type":"current_context"}]}
            END_REQUEST
            BEGIN_REQUEST
            {"version":"1.0","id":"86e31f89-f4e5-4a36-b3fc-f8b11c05ad58","commands":[{"type":"git_status"}]}
            END_REQUEST
            BEGIN_REQUEST
            {"version":"1.0","id":"43436d72-e1e0-40c5-8b3c-580abbd9c0b2","commands":[{"type":"capabilities","command":"get_symbol_source"}]}
            END_REQUEST
            BEGIN_REQUEST
            {"version":"1.0","id":"ac8e2208-88ec-4a72-924d-cd9590c9170a","commands":[{"type":"tree","path":"src/ContextMessenger.Protocol","depth":2,"include":["**/*.cs"]}]}
            END_REQUEST
            """;

        var result = RequestBlockExtractor.Extract(text, "BEGIN_REQUEST", "END_REQUEST");

        Assert.Equal(4, result.Bodies.Count);
        Assert.Contains("a0a25c8c-0979-47da-bf0e-4a835edb9b25", result.Bodies[0]);
        Assert.Contains("86e31f89-f4e5-4a36-b3fc-f8b11c05ad58", result.Bodies[1]);
        Assert.Contains("43436d72-e1e0-40c5-8b3c-580abbd9c0b2", result.Bodies[2]);
        Assert.Contains("ac8e2208-88ec-4a72-924d-cd9590c9170a", result.Bodies[3]);
    }

    [Fact]
    public void Extract_ignores_standalone_documentation_block_that_is_not_request_json()
    {
        var text = """
            Documentation example:
            BEGIN_REQUEST
            A chat client emits JSON here.
            END_REQUEST
            BEGIN_REQUEST
            {"version":"1.0","id":"x","commands":[{"type":"tree"}]}
            END_REQUEST
            """;

        var result = RequestBlockExtractor.Extract(text, "BEGIN_REQUEST", "END_REQUEST");

        var body = Assert.Single(result.Bodies);
        Assert.Contains("\"id\":\"x\"", body);
    }

    [Fact]
    public void Extract_ignores_json_block_that_fails_request_validation()
    {
        var text = """
            BEGIN_REQUEST
            {"not":"a request"}
            END_REQUEST
            BEGIN_REQUEST
            {"version":"1.0","id":"x","commands":[{"type":"tree"}]}
            END_REQUEST
            """;

        var result = RequestBlockExtractor.Extract(text, "BEGIN_REQUEST", "END_REQUEST");

        var body = Assert.Single(result.Bodies);
        Assert.Contains("\"id\":\"x\"", body);
    }

    [Fact]
    public void Extract_reports_invalid_json_candidate_error()
    {
        var text = """
            BEGIN_REQUEST
            {"version":"1.0","id":"x","commands":[{"type":"propose_patch","files":[{"path":"x.cs","operation":"create","newContent":"return "ok";"}]}]}
            END_REQUEST
            """;

        var result = RequestBlockExtractor.Extract(text, "BEGIN_REQUEST", "END_REQUEST");

        Assert.Single(result.Bodies);
        Assert.True(result.HasInvalidJsonCandidate);
        Assert.True(result.ReturnedInvalidBody);
        Assert.Contains("invalid", result.InvalidJsonMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Extract_returns_invalid_json_body_for_unescaped_quotes_in_patch_content()
    {
        var text = """
            BEGIN_REQUEST
            {
              "version": "1.0",
              "id": "x",
              "commands": [
                {
                  "type": "propose_patch",
                  "files": [
                    {
                      "path": "src/Example.cs",
                      "operation": "create",
                      "newContent": "namespace Demo;

            public static class Example
            {
                public static string Value() => "ok";
            }
            "
                    }
                  ]
                }
              ]
            }
            END_REQUEST
            """;

        var result = RequestBlockExtractor.Extract(text, "BEGIN_REQUEST", "END_REQUEST");

        var body = Assert.Single(result.Bodies);
        Assert.Contains("\"newContent\"", body);
        Assert.True(result.HasInvalidJsonCandidate);
        Assert.True(result.ReturnedInvalidBody);
        Assert.Contains("escape quotes", result.InvalidJsonMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Extract_prefers_later_valid_block_over_invalid_json_candidate()
    {
        var text = """
            BEGIN_REQUEST
            {"version":"1.0","id":"bad","commands":[{"type":"tree","x":"return "ok";"}]}
            END_REQUEST
            BEGIN_REQUEST
            {"version":"1.0","id":"good","commands":[{"type":"tree"}]}
            END_REQUEST
            """;

        var result = RequestBlockExtractor.Extract(text, "BEGIN_REQUEST", "END_REQUEST");

        var body = Assert.Single(result.Bodies);
        Assert.Contains("\"id\":\"good\"", body);
        Assert.False(result.ReturnedInvalidBody);
        Assert.True(result.HasInvalidJsonCandidate);
    }

    [Fact]
    public void Extract_with_repair_recovers_unescaped_quotes_in_patch_content()
    {
        var text = """
            BEGIN_REQUEST
            {
              "version": "1.0",
              "id": "x",
              "commands": [
                {
                  "type": "propose_patch",
                  "files": [
                    {
                      "path": "src/Example.cs",
                      "operation": "create",
                      "newContent": "namespace Demo;

            public static class Example
            {
                public static string Value() => "ok";
            }
            "
                    }
                  ]
                }
              ]
            }
            END_REQUEST
            """;

        var result = RequestBlockExtractor.Extract(
            text, "BEGIN_REQUEST", "END_REQUEST", repairUnterminatedQuotes: true);

        var body = Assert.Single(result.Bodies);
        Assert.False(result.ReturnedInvalidBody);
        Assert.False(result.HasInvalidJsonCandidate);

        // The repaired body is what is returned, so the downstream re-parse succeeds.
        var request = Assert.Single(ProtocolParser.ParseBodyAndValidate(body));
        Assert.Equal("x", request.Id);
    }

    [Fact]
    public void Extract_without_repair_flag_keeps_unescaped_quotes_invalid()
    {
        var text = """
            BEGIN_REQUEST
            {
              "version": "1.0",
              "id": "x",
              "commands": [
                {
                  "type": "propose_patch",
                  "files": [
                    {
                      "path": "src/Example.cs",
                      "operation": "create",
                      "newContent": "var s = "ok";"
                    }
                  ]
                }
              ]
            }
            END_REQUEST
            """;

        var result = RequestBlockExtractor.Extract(text, "BEGIN_REQUEST", "END_REQUEST");

        Assert.True(result.ReturnedInvalidBody);
        Assert.True(result.HasInvalidJsonCandidate);
    }

    [Fact]
    public void Extract_with_repair_leaves_valid_body_unchanged()
    {
        var text = """
            BEGIN_REQUEST
            {
              "version": "1.0",
              "id": "good",
              "commands": [
                { "type": "tree", "path": ".", "depth": 2 }
              ]
            }
            END_REQUEST
            """;

        var result = RequestBlockExtractor.Extract(
            text, "BEGIN_REQUEST", "END_REQUEST", repairUnterminatedQuotes: true);

        var body = Assert.Single(result.Bodies);
        // The body is returned verbatim, not normalized through the repair lexer.
        Assert.Contains("\"depth\": 2", body);
        Assert.False(result.HasInvalidJsonCandidate);
    }

    [Fact]
    public void Extract_with_repair_still_rejects_non_quote_structural_errors()
    {
        var text = """
            BEGIN_REQUEST
            {
              "version": "1.0",
              "id": "x",
              "commands": [
            }
            END_REQUEST
            """;

        var result = RequestBlockExtractor.Extract(
            text, "BEGIN_REQUEST", "END_REQUEST", repairUnterminatedQuotes: true);

        // The repair lexer cannot parse a structural error, so it falls back to
        // the original invalid-candidate handling rather than masking it.
        Assert.True(result.ReturnedInvalidBody);
        Assert.True(result.HasInvalidJsonCandidate);
    }

    private static string FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file '{relativePath}'.");
    }
}
