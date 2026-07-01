using System.Text.Json;
using ContextMessenger.Core.Meta;
using ContextMessenger.FileSystem;
using ContextMessenger.Protocol.Commands;
using ContextMessenger.Protocol.Dispatch;
using ContextMessenger.Protocol.Wire;

namespace ContextMessenger.Protocol.Tests.Commands;

public sealed class MetaCommandTests
{
    [Fact]
    public void Current_context_returns_session_state()
    {
        using var temp = new TempDirectory();
        var session = new FakeContextSession(
            root: new RootProfileInfo { Name = "Main", Path = "C:/repo", IsCurrent = true },
            target: new TargetProfileInfo { Name = "ChatGPT", Process = "ChatGPT", IsCurrent = true });
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            roslyn: null,
            session: session);

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands = [new ContextCommand { Type = CommandTypes.CurrentContext }],
        });
        var result = Assert.Single(response.Results!);

        Assert.Equal(ProtocolStatus.Ok, result.Status);
        Assert.Equal(CommandTypes.CurrentContext, result.Type);
        Assert.Equal("Main", result.Payload["rootProfile"].GetProperty("name").GetString());
        Assert.Equal("ChatGPT", result.Payload["target"].GetProperty("name").GetString());
        Assert.True(result.Payload["server"].TryGetProperty("name", out _));
        Assert.True(result.Payload["protocol"].TryGetProperty("supported", out _));
    }

    [Fact]
    public void List_roots_marks_active_root_as_current()
    {
        using var temp = new TempDirectory();
        var session = new FakeContextSession(
            roots:
            [
                new RootProfileInfo { Name = "Main", Path = "C:/main", IsCurrent = true },
                new RootProfileInfo { Name = "Other", Path = "C:/other", IsCurrent = false },
            ]);
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            roslyn: null,
            session: session);

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands = [new ContextCommand { Type = CommandTypes.ListRoots }],
        });
        var result = Assert.Single(response.Results!);

        Assert.Equal(ProtocolStatus.Ok, result.Status);
        var roots = result.Payload["roots"].EnumerateArray().ToArray();
        Assert.Equal(2, roots.Length);
        Assert.Equal("Main", roots[0].GetProperty("name").GetString());
        Assert.True(roots[0].GetProperty("isCurrent").GetBoolean());
        Assert.False(roots[1].GetProperty("isCurrent").GetBoolean());
    }

    [Fact]
    public void Set_root_returns_updated_context_and_calls_session()
    {
        using var temp = new TempDirectory();
        var session = new FakeContextSession(
            roots:
            [
                new RootProfileInfo { Name = "Main", Path = "C:/main", IsCurrent = true },
                new RootProfileInfo { Name = "Other", Path = "C:/other", IsCurrent = false },
            ]);
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            roslyn: null,
            session: session);

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands = [ParamCommand(CommandTypes.SetRoot, new { name = "Other" })],
        });
        var result = Assert.Single(response.Results!);

        Assert.Equal(ProtocolStatus.Ok, result.Status);
        Assert.Equal("Other", session.LastSetRootName);
        Assert.Equal("Other", result.Payload["rootProfile"].GetProperty("name").GetString());
        Assert.True(result.Payload["rootProfile"].GetProperty("isCurrent").GetBoolean());
    }

    [Fact]
    public void Set_root_returns_invalid_parameters_for_unknown_root()
    {
        using var temp = new TempDirectory();
        var session = new FakeContextSession(
            roots: [new RootProfileInfo { Name = "Main", Path = "C:/main", IsCurrent = true }]);
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            roslyn: null,
            session: session);

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands = [ParamCommand(CommandTypes.SetRoot, new { name = "Missing" })],
        });
        var result = Assert.Single(response.Results!);

        Assert.Equal(ProtocolStatus.Error, result.Status);
        Assert.Equal(ProtocolErrorCodes.InvalidParameters, result.Error!.Code);
    }

    [Fact]
    public void Set_root_returns_invalid_parameters_when_name_is_empty()
    {
        using var temp = new TempDirectory();
        var session = new FakeContextSession(
            roots: [new RootProfileInfo { Name = "Main", Path = "C:/main", IsCurrent = true }]);
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            roslyn: null,
            session: session);

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands = [ParamCommand(CommandTypes.SetRoot, new { name = "" })],
        });
        var result = Assert.Single(response.Results!);

        Assert.Equal(ProtocolStatus.Error, result.Status);
        Assert.Equal(ProtocolErrorCodes.InvalidParameters, result.Error!.Code);
    }

    [Fact]
    public void Set_root_intermediate_calls_in_batch_return_ignored_with_reason()
    {
        using var temp = new TempDirectory();
        var session = new FakeContextSession(
            roots:
            [
                new RootProfileInfo { Name = "Main", Path = "C:/main", IsCurrent = true },
                new RootProfileInfo { Name = "Other", Path = "C:/other", IsCurrent = false },
                new RootProfileInfo { Name = "Third", Path = "C:/third", IsCurrent = false },
            ]);
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            roslyn: null,
            session: session);

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands =
            [
                ParamCommand(CommandTypes.SetRoot, new { name = "Other" }),
                new ContextCommand { Type = CommandTypes.CurrentContext },
                ParamCommand(CommandTypes.SetRoot, new { name = "Third" }),
            ],
        });
        var results = response.Results!;

        Assert.Equal(3, results.Count);

        Assert.Equal(ProtocolStatus.Ignored, results[0].Status);
        Assert.Equal(CommandTypes.SetRoot, results[0].Type);
        Assert.True(results[0].Payload.ContainsKey("reason"));
        Assert.Contains("Superseded", results[0].Payload["reason"].GetString());

        Assert.Equal(ProtocolStatus.Ok, results[1].Status);
        Assert.Equal(CommandTypes.CurrentContext, results[1].Type);

        Assert.Equal(ProtocolStatus.Ok, results[2].Status);
        Assert.Equal(CommandTypes.SetRoot, results[2].Type);
        Assert.Equal("Third", results[2].Payload["rootProfile"].GetProperty("name").GetString());
    }

    [Fact]
    public void Single_set_root_in_batch_is_not_ignored()
    {
        using var temp = new TempDirectory();
        var session = new FakeContextSession(
            roots: [new RootProfileInfo { Name = "Main", Path = "C:/main", IsCurrent = true }]);
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            roslyn: null,
            session: session);

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands = [ParamCommand(CommandTypes.SetRoot, new { name = "Main" })],
        });
        var result = Assert.Single(response.Results!);

        Assert.Equal(ProtocolStatus.Ok, result.Status);
    }

    [Fact]
    public void Intermediate_set_root_does_not_mutate_session()
    {
        using var temp = new TempDirectory();
        var session = new FakeContextSession(
            roots:
            [
                new RootProfileInfo { Name = "Main", Path = "C:/main", IsCurrent = true },
                new RootProfileInfo { Name = "Other", Path = "C:/other", IsCurrent = false },
                new RootProfileInfo { Name = "Third", Path = "C:/third", IsCurrent = false },
            ]);
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            roslyn: null,
            session: session);

        dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands =
            [
                ParamCommand(CommandTypes.SetRoot, new { name = "Other" }),
                ParamCommand(CommandTypes.SetRoot, new { name = "Third" }),
            ],
        });

        Assert.Equal("Third", session.LastSetRootName);
    }

    [Fact]
    public void List_targets_returns_session_targets()
    {
        using var temp = new TempDirectory();
        var session = new FakeContextSession(
            target: new TargetProfileInfo { Name = "ChatGPT", Process = "ChatGPT.exe", Description = "ChatGPT Desktop", IsCurrent = true });
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            roslyn: null,
            session: session);

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands = [new ContextCommand { Type = CommandTypes.ListTargets }],
        });
        var result = Assert.Single(response.Results!);

        Assert.Equal(ProtocolStatus.Ok, result.Status);
        var target = Assert.Single(result.Payload["targets"].EnumerateArray());
        Assert.Equal("ChatGPT", target.GetProperty("name").GetString());
        Assert.Equal("ChatGPT.exe", target.GetProperty("process").GetString());
        Assert.Equal("ChatGPT Desktop", target.GetProperty("description").GetString());
        Assert.True(target.GetProperty("isCurrent").GetBoolean());
    }

    [Fact]
    public void Capabilities_with_no_filter_returns_registered_catalog()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForFileSystem(new FileSystemContextService(temp.Path));

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands = [new ContextCommand { Type = CommandTypes.Capabilities }],
        });
        var result = Assert.Single(response.Results!);

        Assert.Equal(ProtocolStatus.Ok, result.Status);
        var commands = result.Payload["commands"].EnumerateArray().ToArray();
        Assert.Equal(dispatcher.RegisteredCommands.Count, commands.Length);
        Assert.Contains(commands, c => c.GetProperty("name").GetString() == CommandTypes.Tree);
        Assert.Contains(commands, c => c.GetProperty("name").GetString() == CommandTypes.Capabilities);
        Assert.DoesNotContain(commands, c => c.GetProperty("name").GetString() == CommandTypes.SetRoot);
        Assert.DoesNotContain(commands, c => c.GetProperty("name").GetString() == CommandTypes.GetSymbolInfo);
    }

    [Fact]
    public void Capabilities_filtered_by_command_returns_single_descriptor()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForFileSystem(new FileSystemContextService(temp.Path));

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands = [ParamCommand(CommandTypes.Capabilities, new { command = CommandTypes.Tree })],
        });
        var result = Assert.Single(response.Results!);

        Assert.Equal(ProtocolStatus.Ok, result.Status);
        var command = Assert.Single(result.Payload["commands"].EnumerateArray());
        Assert.Equal(CommandTypes.Tree, command.GetProperty("name").GetString());
        Assert.Equal(CommandCatalog.CategoryFileSystem, command.GetProperty("category").GetString());
        Assert.Equal(CommandCatalog.SideEffectsNone, command.GetProperty("sideEffects").GetString());
        Assert.Contains(command.GetProperty("parameters").EnumerateArray(), p =>
            p.GetProperty("name").GetString() == "path" && !p.GetProperty("required").GetBoolean());
    }

    [Fact]
    public void Capabilities_response_marks_set_root_with_session_side_effects()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            roslyn: null,
            session: new FakeContextSession());

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands = [ParamCommand(CommandTypes.Capabilities, new { command = CommandTypes.SetRoot })],
        });
        var result = Assert.Single(response.Results!);
        var command = Assert.Single(result.Payload["commands"].EnumerateArray());

        Assert.Equal(CommandCatalog.SideEffectsSession, command.GetProperty("sideEffects").GetString());
    }

    [Theory]
    [InlineData(CommandTypes.ProposePatch)]
    [InlineData(CommandTypes.AmendPatch)]
    [InlineData(CommandTypes.ValidatePatch)]
    public void Capabilities_patch_commands_advertise_textual_edits(string commandName)
    {
        var command = CommandCatalog.Find(commandName);
        Assert.NotNull(command);
        Assert.Contains(command!.Parameters, p => p.Name == "files" && !p.Required);
        Assert.Contains(command.Parameters, p => p.Name == "edits" && !p.Required);

        var feature = Assert.Single(command.Features!, f => f.Name == "edits");
        var values = feature.Values!;
        Assert.Contains("replace_exact", values);
        Assert.Contains("insert_before_exact", values);
        Assert.Contains("insert_after_exact", values);
        Assert.Contains("delete_exact", values);
        Assert.Contains("replace_lines", values);
        Assert.Contains("json_set", values);
        Assert.Contains("replace_symbol_source", values);

        var kinds = feature.Kinds!.ToDictionary(
            k => k.Kind,
            StringComparer.Ordinal);

        AssertEditKind(kinds["replace_exact"],
            required: ["path", "oldText", "newText"],
            optional: ["oldTextEncoding", "newTextEncoding", "expectedFileHash", "expectedAnchorHash"],
            expectedAnchorHashTarget: "oldText");
        AssertEditKind(kinds["insert_before_exact"],
            required: ["path", "anchor", "text"],
            optional: ["anchorEncoding", "textEncoding", "expectedFileHash", "expectedAnchorHash"],
            expectedAnchorHashTarget: "anchor");
        AssertEditKind(kinds["insert_after_exact"],
            required: ["path", "anchor", "text"],
            optional: ["anchorEncoding", "textEncoding", "expectedFileHash", "expectedAnchorHash"],
            expectedAnchorHashTarget: "anchor");
        AssertEditKind(kinds["delete_exact"],
            required: ["path", "oldText"],
            optional: ["oldTextEncoding", "expectedFileHash", "expectedAnchorHash"],
            expectedAnchorHashTarget: "oldText");
        AssertEditKind(kinds["replace_lines"],
            required: ["path", "startLine", "endLine", "oldRangeHash", "newText"],
            optional: ["newTextEncoding", "expectedFileHash"],
            expectedAnchorHashTarget: null);
        AssertEditKind(kinds["json_set"],
            required: ["path", "pointer", "value"],
            optional: ["expectedFileHash"],
            expectedAnchorHashTarget: null);
        AssertEditKind(kinds["replace_symbol_source"],
            required: ["newText", "oldSourceHash"],
            optional: ["newTextEncoding", "symbolId", "name", "match", "kinds", "project", "includeNonPublic", "path", "line", "column", "expectedFileHash"],
            expectedAnchorHashTarget: null);
    }

    private static void AssertEditKind(
        CommandEditKindInfo kind,
        string[] required,
        string[] optional,
        string? expectedAnchorHashTarget)
    {
        Assert.Equal(required, kind.Required);
        Assert.Equal(optional, kind.Optional);
        if (expectedAnchorHashTarget is null)
        {
            Assert.Null(kind.ExpectedAnchorHashTarget);
        }
        else
        {
            Assert.Equal(expectedAnchorHashTarget, kind.ExpectedAnchorHashTarget);
        }
    }

    [Fact]
    public void Capabilities_with_unknown_command_returns_invalid_parameters()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForFileSystem(new FileSystemContextService(temp.Path));

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands = [ParamCommand(CommandTypes.Capabilities, new { command = "no_such_thing" })],
        });
        var result = Assert.Single(response.Results!);

        Assert.Equal(ProtocolStatus.Error, result.Status);
        Assert.Equal(ProtocolErrorCodes.InvalidParameters, result.Error!.Code);
    }

    [Fact]
    public void Capabilities_is_registered_without_a_session()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForFileSystem(new FileSystemContextService(temp.Path));

        Assert.Contains(CommandTypes.Capabilities, dispatcher.RegisteredCommands);
        Assert.DoesNotContain(CommandTypes.ListTargets, dispatcher.RegisteredCommands);
    }

    [Fact]
    public void Dispatcher_registers_meta_handlers_only_when_session_provided()
    {
        using var temp = new TempDirectory();
        var withoutSession = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path));
        Assert.DoesNotContain(CommandTypes.CurrentContext, withoutSession.RegisteredCommands);
        Assert.DoesNotContain(CommandTypes.ListRoots, withoutSession.RegisteredCommands);
        Assert.DoesNotContain(CommandTypes.SetRoot, withoutSession.RegisteredCommands);
        Assert.DoesNotContain(CommandTypes.ListTargets, withoutSession.RegisteredCommands);

        var session = new FakeContextSession();
        var withSession = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            roslyn: null,
            session: session);
        Assert.Contains(CommandTypes.CurrentContext, withSession.RegisteredCommands);
        Assert.Contains(CommandTypes.ListRoots, withSession.RegisteredCommands);
        Assert.Contains(CommandTypes.SetRoot, withSession.RegisteredCommands);
        Assert.Contains(CommandTypes.ListTargets, withSession.RegisteredCommands);
    }

    private static ContextCommand ParamCommand(string type, object parameters)
    {
        var cmd = new ContextCommand { Type = type };
        var element = JsonSerializer.SerializeToElement(parameters);
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
                cmd.Parameters[prop.Name] = prop.Value.Clone();
        }
        return cmd;
    }

    private sealed class FakeContextSession : IContextSession
    {
        private readonly RootProfileInfo _root;
        private readonly TargetProfileInfo _target;
        private readonly IReadOnlyList<RootProfileInfo> _roots;
        private RootProfileInfo _currentRoot;

        public FakeContextSession(
            RootProfileInfo? root = null,
            TargetProfileInfo? target = null,
            IReadOnlyList<RootProfileInfo>? roots = null)
        {
            _root = root ?? new RootProfileInfo { Name = "Main", Path = "C:/main", IsCurrent = true };
            _target = target ?? new TargetProfileInfo { Name = "ChatGPT", Process = "ChatGPT", IsCurrent = true };
            _roots = roots ?? [_root];
            _currentRoot = _root;
        }

        public string? LastSetRootName { get; private set; }

        public CurrentContextInfo GetCurrentContext() => new()
        {
            RootProfile = _currentRoot,
            Target = _target,
            Server = new ServerInfo { Name = "ContextMessenger", Version = "1.0.0.0" },
            Protocol = new ProtocolInfo { Supported = ["1.0"] },
        };

        public IReadOnlyList<RootProfileInfo> ListRoots() => _roots;

        public CurrentContextInfo SetRoot(string name)
        {
            LastSetRootName = name;
            var match = _roots.FirstOrDefault(r =>
                string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new ProtocolException(
                    ProtocolErrorCodes.InvalidParameters,
                    $"Root '{name}' not found.");

            _currentRoot = new RootProfileInfo
            {
                Name = match.Name,
                Path = match.Path,
                Description = match.Description,
                IsCurrent = true,
            };
            return GetCurrentContext();
        }

        public IReadOnlyList<TargetProfileInfo> ListTargets() => [_target];

        public void ApplyPendingRootSwitch() { }
    }
}
