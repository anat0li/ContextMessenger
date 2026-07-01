using System.Reflection;
using ContextMessenger.Core.Meta;
using ContextMessenger.Protocol;
using ContextMessenger.Protocol.Commands;

namespace ContextMessenger.Protocol.Tests;

public sealed class SystemPromptProviderTests
{
    [Fact]
    public void ChatGpt_prompt_markdown_matches_generated_prompt()
    {
        var markdown = File.ReadAllText(FindRepoFile("docs/chat-prompt.md"));
        var fencedPrompt = ExtractTextFence(markdown);

        Assert.Equal(
            NormalizeLineEndings(SystemPromptProvider.Generate()).TrimEnd(),
            NormalizeLineEndings(fencedPrompt).TrimEnd());
    }

    [Fact]
    public void Generate_includes_BEGIN_REQUEST_and_END_REQUEST()
    {
        var prompt = SystemPromptProvider.Generate();

        Assert.Contains(ProtocolDelimiters.BeginRequest, prompt);
        Assert.Contains(ProtocolDelimiters.EndRequest, prompt);
    }

    [Fact]
    public void Generate_lists_all_CommandTypes_constants()
    {
        var prompt = SystemPromptProvider.Generate();
        var commandTypes = typeof(CommandTypes)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);

        foreach (var commandType in commandTypes)
            Assert.Contains(commandType, prompt);
    }

    [Fact]
    public void Generate_identifies_capabilities_as_root_authority()
    {
        var prompt = SystemPromptProvider.Generate();

        Assert.Contains("complete protocol-wide catalog, not a guarantee", prompt);
        Assert.Contains("Call capabilities when first connecting and after set_root", prompt);
        Assert.Contains("treat its returned command list as authoritative", prompt);
        Assert.Contains("do not issue commands absent from that list", prompt);
        Assert.Contains("Unknown or currently unavailable names return invalid_parameters", prompt);
    }

    [Fact]
    public void Generate_lists_all_ProtocolErrorCodes_constants()
    {
        var prompt = SystemPromptProvider.Generate();

        foreach (var errorCode in PublicStringConstants(typeof(ProtocolErrorCodes)))
            Assert.Contains(errorCode, prompt);
    }

    [Fact]
    public void ChatGpt_prompt_markdown_lists_protocol_constants_and_catalog_items()
    {
        var prompt = ExtractTextFence(File.ReadAllText(FindRepoFile("docs/chat-prompt.md")));

        foreach (var commandType in PublicStringConstants(typeof(CommandTypes)))
            Assert.Contains(commandType, prompt);

        foreach (var errorCode in PublicStringConstants(typeof(ProtocolErrorCodes)))
            Assert.Contains(errorCode, prompt);

        foreach (var command in CommandCatalog.GetAll())
        {
            Assert.Contains(command.Name, prompt);
            Assert.Contains(command.Category, prompt);
        }
    }

    [Fact]
    public void ChatGpt_prompt_markdown_documents_patch_edit_catalog()
    {
        var prompt = ExtractTextFence(File.ReadAllText(FindRepoFile("docs/chat-prompt.md")));
        var editFeature = PatchEditFeature();

        Assert.NotNull(editFeature.Values);
        foreach (var value in editFeature.Values!)
            Assert.Contains(value, prompt);

        Assert.NotNull(editFeature.Kinds);
        foreach (var kind in editFeature.Kinds!)
        {
            Assert.Contains(kind.Kind, prompt);
            foreach (var required in kind.Required)
                Assert.Contains(required, prompt);
            foreach (var optional in kind.Optional)
                Assert.Contains(optional, prompt);
            if (kind.ExpectedAnchorHashTarget is not null)
                Assert.Contains(kind.ExpectedAnchorHashTarget, prompt);
        }

        foreach (var hashField in new[]
        {
            "oldContentHash",
            "expectedFileHash",
            "expectedAnchorHash",
            "oldRangeHash",
            "oldSourceHash",
            "hashField",
            "expectedHash",
            "actualHash",
            "hashTarget",
            "expectedFormat",
            "warnings",
        })
        {
            Assert.Contains(hashField, prompt);
        }
    }

    [Fact]
    public void ChatGpt_prompt_markdown_documents_patch_policies()
    {
        var prompt = ExtractTextFence(File.ReadAllText(FindRepoFile("docs/chat-prompt.md")));

        Assert.Contains("policy none or solution", prompt);
        Assert.Contains("policy none, all, projects, or filter", prompt);
        Assert.Contains("none      skip tests", prompt);
        Assert.Contains("all       run dotnet test", prompt);
        Assert.Contains("projects  run dotnet test", prompt);
        Assert.Contains("filter    run dotnet test", prompt);
        Assert.Contains("invalid_patch_policy", prompt);
        Assert.Contains("unsupported_patch_policy", prompt);
    }

    [Fact]
    public void Generate_documents_patch_edit_surface()
    {
        var prompt = SystemPromptProvider.Generate();

        Assert.Contains("files          array, optional when edits is non-empty", prompt);
        Assert.Contains("edits          array, optional when files is non-empty", prompt);
        Assert.Contains("At least one of files or edits is required", prompt);
        Assert.Contains("Files apply first, then edits in array order", prompt);

        foreach (var editKind in new[]
        {
            "replace_exact",
            "insert_before_exact",
            "insert_after_exact",
            "delete_exact",
            "replace_lines",
            "json_set",
            "replace_symbol_source",
        })
        {
            Assert.Contains(editKind, prompt);
        }

        Assert.Contains("Text matching is literal and must match exactly once", prompt);
        Assert.Contains("expectedAnchorHash guards replace_exact/delete_exact oldText or insert anchor", prompt);
        Assert.Contains("error.hashField", prompt);
        Assert.Contains("error.expectedHash", prompt);
        Assert.Contains("error.matches", prompt);
        Assert.Contains("error.lineEndingHint", prompt);
        Assert.Contains("line is 1-based and column is 0-based", prompt);
        Assert.Contains("per-kind required/optional field metadata", prompt);
        Assert.Contains("warnings      array, omitted when empty", prompt);
        Assert.Contains("LF/CRLF-flexible matching", prompt);
        Assert.Contains("prefer anchors without a line terminator", prompt);
        Assert.Contains("oldRangeHash over the exact replaced slice", prompt);
        Assert.Contains("json_set uses RFC 6901 JSON Pointer", prompt);
        Assert.Contains("replace_symbol_source reuses get_symbol_source selection", prompt);
    }

    [Fact]
    public void Generate_documents_patch_policy_failure_boundary()
    {
        var prompt = SystemPromptProvider.Generate();

        Assert.Contains("invalid build/tests policy", prompt);
        Assert.Contains("metadata is persisted as `needs_revision`", prompt);
        Assert.Contains("Malformed request shape, patch apply failures, and diff verification failures still return immediate command errors", prompt);
        Assert.Contains("do not create a new active patch", prompt);
        Assert.Contains("Defaults to the previous build policy", prompt);
        Assert.Contains("Defaults to the previous tests policy", prompt);
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

    private static string ExtractTextFence(string markdown)
    {
        const string fenceStart = "```text";
        const string fenceEnd = "```";

        var start = markdown.IndexOf(fenceStart, StringComparison.Ordinal);
        Assert.True(start >= 0, "Expected a ```text fence.");

        var contentStart = markdown.IndexOf('\n', start);
        Assert.True(contentStart >= 0, "Expected fenced content after ```text.");
        contentStart++;

        var end = markdown.IndexOf(fenceEnd, contentStart, StringComparison.Ordinal);
        Assert.True(end >= 0, "Expected closing ``` fence.");

        return markdown[contentStart..end];
    }

    private static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static IEnumerable<string> PublicStringConstants(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);

    private static CommandFeatureInfo PatchEditFeature()
    {
        var command = CommandCatalog.Find(CommandTypes.ProposePatch)
            ?? throw new InvalidOperationException("propose_patch is missing from the command catalog.");

        return command.Features?.Single(f => f.Name == "edits")
            ?? throw new InvalidOperationException("propose_patch is missing the edits feature.");
    }
}
