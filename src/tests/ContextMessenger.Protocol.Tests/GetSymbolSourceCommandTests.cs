using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ContextMessenger.Core.Roslyn;
using ContextMessenger.FileSystem;
using ContextMessenger.Protocol.Commands;
using ContextMessenger.Protocol.Dispatch;
using ContextMessenger.Protocol.Wire;

namespace ContextMessenger.Protocol.Tests;

public sealed class GetSymbolSourceCommandTests
{
    [Fact]
    public void ForServices_registers_get_symbol_source_when_roslyn_service_is_available()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            new FakeRoslynNavigationService());

        Assert.Contains(CommandTypes.GetSymbolSource, dispatcher.RegisteredCommands);
    }

    [Fact]
    public void Dispatch_executes_get_symbol_source_by_symbol_id()
    {
        using var temp = new TempDirectory();
        temp.CreateFile(
            "src/Parser.cs",
            """
            namespace Demo;

            public static class Parser
            {
                public static void Parse(string text)
                {
                    _ = text;
                }

                public static void Other()
                {
                }
            }
            """);
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            new FakeRoslynNavigationService());

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands =
            [
                ParamCommand(CommandTypes.GetSymbolSource, new
                {
                    symbolId = "M:Demo.Parser.Parse(System.String)",
                }),
            ],
        });

        var result = Assert.Single(response.Results!);
        Assert.Equal(ProtocolStatus.Ok, result.Status);
        Assert.Equal(CommandTypes.GetSymbolSource, result.Type);
        Assert.Equal("sha256:test", result.Payload["workspaceVersion"].GetString());
        Assert.Equal("Parse", result.Payload["symbol"].GetProperty("name").GetString());

        var source = result.Payload["source"];
        Assert.Equal("src/Parser.cs", source.GetProperty("path").GetString());
        Assert.Equal(5, source.GetProperty("startLine").GetInt32());
        Assert.Equal(8, source.GetProperty("endLine").GetInt32());
        Assert.Contains("public static void Parse(string text)", source.GetProperty("text").GetString());
        Assert.DoesNotContain("public static void Other", source.GetProperty("text").GetString());
        var expectedHash = HashText(source.GetProperty("text").GetString()!);
        Assert.Equal(expectedHash, source.GetProperty("hash").GetString());
        Assert.Equal(expectedHash, source.GetProperty("oldSourceHash").GetString());
    }

    [Fact]
    public void Dispatch_executes_get_symbol_source_by_location()
    {
        using var temp = new TempDirectory();
        temp.CreateFile(
            "src/Parser.cs",
            """
            namespace Demo;

            public static class Parser
            {
                public static void Parse(string text)
                {
                    _ = text;
                }
            }
            """);
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            new FakeRoslynNavigationService());

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands =
            [
                ParamCommand(CommandTypes.GetSymbolSource, new
                {
                    path = "src/Parser.cs",
                    line = 5,
                    column = 31,
                }),
            ],
        });

        var result = Assert.Single(response.Results!);
        Assert.Equal(ProtocolStatus.Ok, result.Status);
        Assert.Equal("Parse", result.Payload["symbol"].GetProperty("name").GetString());
    }

    [Fact]
    public void Dispatch_executes_get_symbol_source_by_name()
    {
        using var temp = new TempDirectory();
        var roslyn = new FakeRoslynNavigationService();
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            roslyn);

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands =
            [
                ParamCommand(CommandTypes.GetSymbolSource, new
                {
                    name = "Parse",
                    kinds = new[] { "method" },
                    includeNonPublic = true,
                }),
            ],
        });

        var result = Assert.Single(response.Results!);
        Assert.Equal(ProtocolStatus.Ok, result.Status);
        Assert.Equal("Parse", result.Payload["symbol"].GetProperty("name").GetString());
        Assert.Contains("public static void Parse", result.Payload["source"].GetProperty("text").GetString());
        Assert.Equal("Parse", roslyn.LastGetSymbolSourceQuery?.Name);
        Assert.Equal(["method"], roslyn.LastGetSymbolSourceQuery?.Kinds);
        Assert.True(roslyn.LastGetSymbolSourceQuery?.IncludeNonPublic);
    }

    [Fact]
    public void Dispatch_rejects_get_symbol_source_when_source_exceeds_max_bytes()
    {
        using var temp = new TempDirectory();
        temp.CreateFile(
            "src/Parser.cs",
            """
            namespace Demo;

            public static class Parser
            {
                public static void Parse(string text)
                {
                    _ = text;
                }
            }
            """);
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            new FakeRoslynNavigationService());

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands =
            [
                ParamCommand(CommandTypes.GetSymbolSource, new
                {
                    symbolId = "M:Demo.Parser.Parse(System.String)",
                    maxBytes = 8,
                }),
            ],
        });

        var result = Assert.Single(response.Results!);
        Assert.Equal(ProtocolStatus.Error, result.Status);
        Assert.Equal(ProtocolErrorCodes.InvalidParameters, result.Error!.Code);
        Assert.Contains("maxBytes", result.Error.Message);
    }

    [Fact]
    public void Dispatch_rejects_get_symbol_source_when_symbol_id_and_location_are_both_provided()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            new FakeRoslynNavigationService());

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands =
            [
                ParamCommand(CommandTypes.GetSymbolSource, new
                {
                    symbolId = "M:Demo.Parser.Parse(System.String)",
                    path = "src/Parser.cs",
                    line = 5,
                    column = 31,
                }),
            ],
        });

        var result = Assert.Single(response.Results!);
        Assert.Equal(ProtocolStatus.Error, result.Status);
        Assert.Equal(ProtocolErrorCodes.InvalidParameters, result.Error!.Code);
        Assert.Contains("exactly one selector", result.Error.Message);
    }

    [Fact]
    public void Dispatch_rejects_get_symbol_source_when_no_selector_is_provided()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            new FakeRoslynNavigationService());

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands =
            [
                ParamCommand(CommandTypes.GetSymbolSource, new
                {
                    maxLines = 20,
                }),
            ],
        });

        var result = Assert.Single(response.Results!);
        Assert.Equal(ProtocolStatus.Error, result.Status);
        Assert.Equal(ProtocolErrorCodes.InvalidParameters, result.Error!.Code);
        Assert.Contains("symbolId, name, or path/line/column is required", result.Error.Message);
    }

    [Fact]
    public void Dispatch_rejects_get_symbol_source_when_location_is_partial()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            new FakeRoslynNavigationService());

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands =
            [
                ParamCommand(CommandTypes.GetSymbolSource, new
                {
                    path = "src/Parser.cs",
                    line = 5,
                }),
            ],
        });

        var result = Assert.Single(response.Results!);
        Assert.Equal(ProtocolStatus.Error, result.Status);
        Assert.Equal(ProtocolErrorCodes.InvalidParameters, result.Error!.Code);
        Assert.Contains("path, line, and column are required together", result.Error.Message);
    }

    [Fact]
    public void Dispatch_rejects_get_symbol_source_when_name_and_symbol_id_are_both_provided()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            new FakeRoslynNavigationService());

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands =
            [
                ParamCommand(CommandTypes.GetSymbolSource, new
                {
                    symbolId = "M:Demo.Parser.Parse(System.String)",
                    name = "Parse",
                }),
            ],
        });

        var result = Assert.Single(response.Results!);
        Assert.Equal(ProtocolStatus.Error, result.Status);
        Assert.Equal(ProtocolErrorCodes.InvalidParameters, result.Error!.Code);
        Assert.Contains("exactly one selector", result.Error.Message);
    }

    private static ContextCommand ParamCommand(string type, object parameters)
    {
        var cmd = new ContextCommand { Type = type };
        var element = JsonSerializer.SerializeToElement(parameters);
        foreach (var prop in element.EnumerateObject())
            cmd.Parameters[prop.Name] = prop.Value.Clone();

        return cmd;
    }

    private static string HashText(string text) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private sealed class FakeRoslynNavigationService : IRoslynNavigationService
    {
        public GetSymbolSourceQuery? LastGetSymbolSourceQuery { get; private set; }

        public string GetWorkspaceVersion() => "sha256:test";

        public void InvalidateWorkspace()
        {
        }

        public DocumentSymbolsResult GetDocumentSymbols(DocumentSymbolsQuery query) => throw new NotSupportedException();

        public FindSymbolsResult FindSymbols(FindSymbolQuery query) => throw new NotSupportedException();

        public FindReferencesResult FindReferences(FindReferencesQuery query) => throw new NotSupportedException();

        public GotoDefinitionResult GotoDefinition(GotoDefinitionQuery query) => new()
        {
            WorkspaceVersion = GetWorkspaceVersion(),
            Definitions = [ParseSymbol()],
        };

        public FindImplementationsResult FindImplementations(FindImplementationsQuery query) => throw new NotSupportedException();

        public FindCallersResult FindCallers(FindCallersQuery query) => throw new NotSupportedException();

        public FindDerivedTypesResult FindDerivedTypes(FindDerivedTypesQuery query) => throw new NotSupportedException();

        public FindOverridesResult FindOverrides(FindOverridesQuery query) => throw new NotSupportedException();

        public SymbolInfoResult GetSymbolInfo(GetSymbolInfoQuery query) => new()
        {
            WorkspaceVersion = GetWorkspaceVersion(),
            Symbol = ParseSymbol(),
        };

        public GetSymbolSourceResult GetSymbolSource(GetSymbolSourceQuery query)
        {
            LastGetSymbolSourceQuery = query;
            if (query.MaxBytes < 64)
                throw new ArgumentOutOfRangeException(nameof(query), "Declaration source exceeds maxBytes; increase maxBytes.");

            return new GetSymbolSourceResult
            {
                WorkspaceVersion = GetWorkspaceVersion(),
                Symbol = ParseSymbol(),
                Source = new SymbolSourceBlock
                {
                    Path = "src/Parser.cs",
                    StartLine = 5,
                    StartColumn = 5,
                    EndLine = 8,
                    EndColumn = 6,
                    Text =
                        """
                            public static void Parse(string text)
                            {
                                _ = text;
                            }
                        """,
                },
            };
        }

        private static SymbolSummary ParseSymbol() => new()
        {
            Name = "Parse",
            Kind = "method",
            SymbolId = "M:Demo.Parser.Parse(System.String)",
            ProjectName = "Demo",
            Path = "src/Parser.cs",
            Line = 5,
            Signature = "public static void Parse(string text)",
            Namespace = "Demo",
            ContainingType = "Parser",
            Accessibility = "public",
        };
    }
}
