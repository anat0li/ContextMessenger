using ContextMessenger.Protocol.Json;

namespace ContextMessenger.Protocol.Tests.Json;

public sealed class LexerTests
{
    [Fact]
    public void Escape_repairs_unescaped_quotes_in_real_request()
    {
        // A real Claude-generated propose_patch request that embeds source code
        // with unescaped quotes in the free-text edit fields. Each value sits on
        // its own line, so the newline-termination heuristic can fold the inner
        // quotes into the string value.
        var input = @"{
""version"": ""1.0"",
""id"": ""c4e6a8b0-3f5d-4c27-9a49-5b6c7d8e9f01"",
""commands"": [
{
""type"": ""propose_patch"",
""title"": ""Add per-root build/test execution gate (AllowPatchExecution)"",
""description"": ""Introduces RootProfile.AllowPatchExecution (default false) and threads it into PatchTransactionService. Propose/Amend now refuse build or test execution with patch_execution_disabled when the root is not armed, before any file mutation; file-only patches remain allowed. The service ctor defaults allowExecution to true for test/library ergonomics, while SessionFactory passes the root flag so production is locked by default at the composition root."",
""commitMessage"": ""Gate patch build/test execution behind RootProfile.AllowPatchExecution"",
""edits"": [
{
""kind"": ""insert_after_exact"",
""path"": ""src/ContextMessenger.App.Wpf/Settings/RootProfile.cs"",
""anchor"": ""    public string? Description { get; init; }"",
""text"": ""\n\n    public bool AllowPatchExecution { get; init; }""
},
{
""kind"": ""replace_exact"",
""path"": ""src/ContextMessenger.App.Wpf/Services/SessionFactory.cs"",
""oldText"": ""            roslynNavigation: roslyn);"",
""newText"": ""            roslynNavigation: roslyn,\n            allowExecution: root.AllowPatchExecution);""
},
{
""kind"": ""insert_after_exact"",
""path"": ""src/ContextMessenger.Patching/PatchTransactionService.cs"",
""anchor"": ""    private PatchSessionMetadata? _foreignActive;"",
""text"": ""\n    private readonly bool _allowExecution;""
},
{
""kind"": ""replace_exact"",
""path"": ""src/ContextMessenger.Patching/PatchTransactionService.cs"",
""oldText"": ""        IPatchDiffVerifier? diffVerifier = null)"",
""newText"": ""        IPatchDiffVerifier? diffVerifier = null,\n        bool allowExecution = true)""
},
{
""kind"": ""insert_after_exact"",
""path"": ""src/ContextMessenger.Patching/PatchTransactionService.cs"",
""anchor"": ""        _diffVerifier = diffVerifier ?? new DefaultPatchDiffVerifier();"",
""text"": ""\n        _allowExecution = allowExecution;""
},
{
""kind"": ""insert_after_exact"",
""path"": ""src/ContextMessenger.Patching/PatchTransactionService.cs"",
""anchor"": ""            throw new PatchValidationException(""invalid_parameters"", ""propose_patch requires at least one file operation or edit."");"",
""text"": ""\n\n        if (!_allowExecution\n            && (!string.Equals(request.Build.Policy, ""none"", StringComparison.OrdinalIgnoreCase)\n                || !string.Equals(request.Tests.Policy, ""none"", StringComparison.OrdinalIgnoreCase)))\n            throw new PatchValidationException(\n                ""patch_execution_disabled"",\n                ""This root is not configured to run build or test execution for patches. Set AllowPatchExecution on the root, or resubmit with build and tests set to policy none."");""
},
{
""kind"": ""insert_after_exact"",
""path"": ""src/ContextMessenger.Patching/PatchTransactionService.cs"",
""anchor"": ""        var testsPolicy = request.Tests ?? _active.TestPolicy;"",
""text"": ""\n\n        if (!_allowExecution\n            && (!string.Equals(buildPolicy.Policy, ""none"", StringComparison.OrdinalIgnoreCase)\n                || !string.Equals(testsPolicy.Policy, ""none"", StringComparison.OrdinalIgnoreCase)))\n            throw new PatchValidationException(\n                ""patch_execution_disabled"",\n                ""This root is not configured to run build or test execution for patches. Set AllowPatchExecution on the root, or resubmit with build and tests set to policy none."");""
},
{
""kind"": ""insert_after_exact"",
""path"": ""src/tests/ContextMessenger.Patching.Tests/PatchTransactionServiceTests.cs"",
""anchor"": ""public sealed class PatchTransactionServiceTests\n{"",
""text"": ""\n\n    [Fact]\n    public void Propose_refuses_build_or_test_execution_when_root_not_armed()\n    {\n        using var temp = CreateRepo();\n        var service = new PatchTransactionService(temp.Path, ""test"", allowExecution: false);\n\n        var ex = Assert.Throws<PatchValidationException>(() => service.Propose(new ProposePatchRequest\n        {\n            Files = [new PatchFileOperation { Path = ""new.txt"", Operation = PatchFileOperationKind.Create, NewContent = ""new"" }],\n            Build = new PatchPolicy { Policy = ""solution"" },\n        }));\n\n        Assert.Equal(""patch_execution_disabled"", ex.Code);\n        Assert.Equal(""none"", service.Current().PatchStatus);\n        Assert.True(new LibGit2SharpGitStatusService(temp.Path).GetStatus().IsClean);\n        Assert.False(File.Exists(Path.Combine(temp.Path, ""new.txt"")));\n    }\n\n    [Fact]\n    public void Propose_allows_file_only_patch_when_root_not_armed()\n    {\n        using var temp = CreateRepo((""file.txt"", ""old""));\n        var filePath = Path.Combine(temp.Path, ""file.txt"");\n        var service = new PatchTransactionService(temp.Path, ""test"", allowExecution: false);\n\n        var result = service.Propose(new ProposePatchRequest\n        {\n            Files =\n            [\n                new PatchFileOperation\n                {\n                    Path = ""file.txt"",\n                    Operation = PatchFileOperationKind.Replace,\n                    OldContentHash = ContentHash.ForFile(filePath),\n                    NewContent = ""new"",\n                },\n            ],\n        });\n\n        Assert.Equal(""accepted"", result.PatchStatus);\n        Assert.Equal(""new"", File.ReadAllText(filePath));\n    }""
}
],
""build"": { ""policy"": ""solution"", ""configuration"": ""Debug"", ""timeoutSeconds"": 240 },
""tests"": {
""policy"": ""projects"",
""projects"": [""src/tests/ContextMessenger.Patching.Tests/ContextMessenger.Patching.Tests.csproj""],
""timeoutSeconds"": 240
}
}
]
}";

        var escaped = Lexer.Escape(input);

        var request = Assert.Single(ProtocolParser.ParseBodyAndValidate(escaped));
        Assert.Equal("c4e6a8b0-3f5d-4c27-9a49-5b6c7d8e9f01", request.Id);
    }

    [Fact]
    public void Escape_preserves_multi_digit_number_lexemes()
    {
        // Regression: the original reader dropped the first digit (240 -> 40).
        var input = """
            {
              "version": "1.0",
              "id": "11111111-1111-1111-1111-111111111111",
              "commands": [
                { "type": "tree", "path": ".", "depth": 240 }
              ]
            }
            """;

        var escaped = Lexer.Escape(input);

        Assert.Contains("240", escaped);
        var request = Assert.Single(ProtocolParser.ParseBodyAndValidate(escaped));
        Assert.Single(request.Commands);
    }

    [Theory]
    [InlineData("3.14")]
    [InlineData("-42")]
    [InlineData("1e10")]
    [InlineData("2.5e-3")]
    public void Escape_passes_number_lexemes_through_verbatim(string number)
    {
        var input = $$"""{ "value": {{number}} }""";

        var escaped = Lexer.Escape(input);

        Assert.Contains(number, escaped);
    }

    [Fact]
    public void Escape_preserves_keyword_literals()
    {
        // Regression: the keyword reader dropped the first letter (null -> ull).
        var input = """{ "a": true, "b": false, "c": null }""";

        var escaped = Lexer.Escape(input);

        Assert.Contains("true", escaped);
        Assert.Contains("false", escaped);
        Assert.Contains("null", escaped);
    }

    [Fact]
    public void Escape_leaves_single_line_nested_object_values_intact()
    {
        // Single-line values are not in the newline-terminated key set, so a
        // quoted value on the same line as its siblings must not be over-consumed.
        var input = """{ "build": { "policy": "solution", "configuration": "Debug" } }""";

        var escaped = Lexer.Escape(input);

        Assert.Contains("solution", escaped);
        Assert.Contains("Debug", escaped);
    }
}
