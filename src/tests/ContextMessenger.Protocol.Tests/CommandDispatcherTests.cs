using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using ContextMessenger.Core.FileSystem;
using ContextMessenger.Core.Meta;
using ContextMessenger.Core.Patching;
using ContextMessenger.Core.Roslyn;
using ContextMessenger.Protocol.Compression;
using ContextMessenger.FileSystem;
using ContextMessenger.Patching;
using ContextMessenger.Protocol.Commands;
using ContextMessenger.Protocol.Dispatch;
using ContextMessenger.Protocol.Wire;
using LibGit2Sharp;

namespace ContextMessenger.Protocol.Tests;

public sealed class CommandDispatcherTests
{
    [Fact]
    public void ForFileSystem_registers_all_FileSystem_commands()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForFileSystem(new FileSystemContextService(temp.Path));
        var commands = dispatcher.RegisteredCommands.ToHashSet();

        Assert.Contains(CommandTypes.Tree, commands);
        Assert.Contains(CommandTypes.ReadFile, commands);
        Assert.Contains(CommandTypes.SearchText, commands);
        Assert.Contains(CommandTypes.ListFiles, commands);
        Assert.Contains(CommandTypes.ProjectInfo, commands);
        Assert.DoesNotContain(CommandTypes.DocumentSymbols, commands);
    }

    [Fact]
    public void ForServices_registers_document_symbols_when_roslyn_service_is_available()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            new FakeRoslynNavigationService());
        var commands = dispatcher.RegisteredCommands.ToHashSet();

        Assert.Contains(CommandTypes.DocumentSymbols, commands);
        Assert.Contains(CommandTypes.FindSymbol, commands);
        Assert.Contains(CommandTypes.FindReferences, commands);
        Assert.Contains(CommandTypes.GotoDefinition, commands);
        Assert.Contains(CommandTypes.FindImplementations, commands);
        Assert.Contains(CommandTypes.FindCallers, commands);
        Assert.Contains(CommandTypes.FindDerivedTypes, commands);
        Assert.Contains(CommandTypes.FindOverrides, commands);
        Assert.Contains(CommandTypes.GetSymbolInfo, commands);
    }

    [Fact]
    public void Dispatch_executes_tree_command_end_to_end()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("src/a.cs", "x");
        var dispatcher = CommandDispatcher.ForFileSystem(new FileSystemContextService(temp.Path));

        var request = new ContextRequest
        {
            Id = "abc",
            Commands = [ParamCommand(CommandTypes.Tree, new { path = ".", depth = 2 })],
        };
        var response = dispatcher.Dispatch(request);

        Assert.Equal(ProtocolStatus.Ok, response.Status);
        var result = Assert.Single(response.Results!);
        Assert.Equal(ProtocolStatus.Ok, result.Status);
        Assert.Equal(0, result.CommandIndex);
        Assert.Equal(CommandTypes.Tree, result.Type);
        Assert.True(result.Payload.ContainsKey("content"));
        var content = result.Payload["content"].GetString();
        Assert.NotNull(content);
        Assert.Contains("src/", content);
        Assert.Contains("a.cs", content);
    }

    [Fact]
    public void Dispatch_executes_read_file_with_content_and_metadata()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("src/foo.cs", "line1\nline2\nline3");
        var dispatcher = CommandDispatcher.ForFileSystem(new FileSystemContextService(temp.Path));

        var request = new ContextRequest
        {
            Id = "abc",
            Commands = [ParamCommand(CommandTypes.ReadFile, new { path = "src/foo.cs" })],
        };
        var response = dispatcher.Dispatch(request);
        var result = Assert.Single(response.Results!);

        Assert.Equal(ProtocolStatus.Ok, result.Status);
        Assert.Equal("line1\nline2\nline3", result.Payload["content"].GetString());
        Assert.Equal(3, result.Payload["lineCount"].GetInt32());
        Assert.False(result.Payload["isTruncated"].GetBoolean());
        Assert.StartsWith("sha256:", result.Payload["contentHash"].GetString());
    }

    [Fact]
    public void Dispatch_read_file_line_slice_returns_range_hash_and_line_ending()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("src/foo.cs", "line1\r\nline2\r\nline3\r\n");
        var dispatcher = CommandDispatcher.ForFileSystem(new FileSystemContextService(temp.Path));

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands = [ParamCommand(CommandTypes.ReadFile, new { path = "src/foo.cs", startLine = 2, endLine = 2 })],
        });
        var result = Assert.Single(response.Results!);

        Assert.Equal(ProtocolStatus.Ok, result.Status);
        Assert.Equal("line2\r\n", result.Payload["content"].GetString());
        Assert.StartsWith("sha256:", result.Payload["rangeHash"].GetString());
        Assert.Equal(2, result.Payload["rangeStartLine"].GetInt32());
        Assert.Equal(2, result.Payload["rangeEndLine"].GetInt32());
        Assert.True(result.Payload["rangeIncludesEndLineTerminator"].GetBoolean());
        Assert.Equal("crlf", result.Payload["lineEnding"].GetString());
    }

    [Fact]
    public void Dispatch_executes_git_status()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            roslyn: null,
            session: null,
            gitStatus: new FakeGitStatusService());

        var request = new ContextRequest
        {
            Id = "abc",
            Commands = [ParamCommand(CommandTypes.GitStatus, new { })],
        };
        var response = dispatcher.Dispatch(request);
        var result = Assert.Single(response.Results!);

        Assert.Equal(ProtocolStatus.Ok, result.Status);
        Assert.Equal(CommandTypes.GitStatus, result.Type);
        Assert.True(result.Payload["isRepository"].GetBoolean());
        Assert.False(result.Payload["isClean"].GetBoolean());
        Assert.Equal("main", result.Payload["branch"].GetString());
        var changed = Assert.Single(result.Payload["changedFiles"].EnumerateArray());
        Assert.Equal("src/file.cs", changed.GetProperty("path").GetString());
        Assert.Equal("modified_unstaged", changed.GetProperty("status").GetString());
    }

    [Fact]
    public void Dispatch_executes_patch_transaction_commands()
    {
        using var temp = new TempDirectory();
        var patches = new FakePatchTransactionService();
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            roslyn: null,
            session: null,
            gitStatus: null,
            patchTransactions: patches);

        var propose = dispatcher.Dispatch(new ContextRequest
        {
            Id = "propose",
            Commands =
            [
                ParamCommand(CommandTypes.ProposePatch, new
                {
                    title = "Patch",
                    files = new object[]
                    {
                        new
                        {
                            path = "src/file.txt",
                            operation = "replace",
                            oldContentHash = "sha256:" + new string('0', 64),
                            newContent = "new",
                        },
                    },
                    build = new { policy = "none" },
                    tests = new { policy = "none" },
                }),
            ],
        });

        var proposeResult = Assert.Single(propose.Results!);
        Assert.Equal(ProtocolStatus.Ok, proposeResult.Status);
        Assert.Equal(CommandTypes.ProposePatch, proposeResult.Type);
        Assert.Equal("accepted", proposeResult.Payload["patchStatus"].GetString());
        Assert.Equal("p-test", proposeResult.Payload["patchId"].GetString());
        Assert.False(proposeResult.Payload.ContainsKey("warnings"));
        Assert.Equal("src/file.txt", patches.ProposedRequest!.Files.Single().Path);
        Assert.Equal(PatchFileOperationKind.Replace, patches.ProposedRequest.Files.Single().Operation);

        dispatcher.Dispatch(new ContextRequest
        {
            Id = "propose-edits",
            Commands =
            [
                ParamCommand(CommandTypes.ProposePatch, new
                {
                    edits = new[]
                    {
                        new
                        {
                            path = "src/file.txt",
                            kind = "replace_exact",
                            oldText = "old",
                            newText = "new",
                            expectedFileHash = "sha256:" + new string('0', 64),
                            expectedAnchorHash = "sha256:" + new string('1', 64),
                        },
                    },
                }),
            ],
        });
        var proposeEdit = Assert.Single(patches.ProposedRequest.Edits);
        Assert.Equal("src/file.txt", proposeEdit.Path);
        Assert.Equal("replace_exact", proposeEdit.Kind);
        Assert.Equal("old", proposeEdit.OldText);
        Assert.Equal("new", proposeEdit.NewText);
        Assert.Equal("sha256:" + new string('0', 64), proposeEdit.ExpectedFileHash);
        Assert.Equal("sha256:" + new string('1', 64), proposeEdit.ExpectedAnchorHash);

        var encodedOldText = Convert.ToBase64String(Encoding.UTF8.GetBytes("const string NewLine = \"\\n\";"));
        var encodedNewText = Convert.ToBase64String(Encoding.UTF8.GetBytes("const string NewLine = \"\\r\\n\";"));
        dispatcher.Dispatch(new ContextRequest
        {
            Id = "propose-encoded-edit",
            Commands =
            [
                ParamCommand(CommandTypes.ProposePatch, new
                {
                    edits = new[]
                    {
                        new
                        {
                            path = "src/file.txt",
                            kind = "replace_exact",
                            oldTextEncoding = "base64utf8",
                            oldText = encodedOldText,
                            newTextEncoding = "base64utf8",
                            newText = encodedNewText,
                        },
                    },
                }),
            ],
        });
        var encodedEdit = Assert.Single(patches.ProposedRequest.Edits);
        Assert.Equal("const string NewLine = \"\\n\";", encodedEdit.OldText);
        Assert.Equal("const string NewLine = \"\\r\\n\";", encodedEdit.NewText);

        dispatcher.Dispatch(new ContextRequest
        {
            Id = "propose-json-set",
            Commands =
            [
                ParamCommand(CommandTypes.ProposePatch, new
                {
                    edits = new[]
                    {
                        new
                        {
                            path = "appsettings.json",
                            kind = "json_set",
                            pointer = "/Feature/Enabled",
                            value = true,
                        },
                    },
                }),
            ],
        });
        var jsonSetEdit = Assert.Single(patches.ProposedRequest.Edits);
        Assert.Equal("json_set", jsonSetEdit.Kind);
        Assert.Equal("/Feature/Enabled", jsonSetEdit.Pointer);
        Assert.True(jsonSetEdit.ValueSpecified);
        Assert.True(jsonSetEdit.Value!.GetValue<bool>());

        dispatcher.Dispatch(new ContextRequest
        {
            Id = "propose-symbol-source",
            Commands =
            [
                ParamCommand(CommandTypes.ProposePatch, new
                {
                    edits = new[]
                    {
                        new
                        {
                            kind = "replace_symbol_source",
                            symbolId = "M:Demo.Parser.Parse",
                            oldSourceHash = "sha256:" + new string('2', 64),
                            newText = "public string Parse() => \"new\";",
                        },
                    },
                }),
            ],
        });
        var symbolEdit = Assert.Single(patches.ProposedRequest.Edits);
        Assert.Equal("replace_symbol_source", symbolEdit.Kind);
        Assert.Equal("M:Demo.Parser.Parse", symbolEdit.SymbolId);
        Assert.Equal("sha256:" + new string('2', 64), symbolEdit.OldSourceHash);
        Assert.Equal("public string Parse() => \"new\";", symbolEdit.NewText);

        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("return \"ok\";"));
        dispatcher.Dispatch(new ContextRequest
        {
            Id = "encoded",
            Commands =
            [
                ParamCommand(CommandTypes.ProposePatch, new
                {
                    files = new object[]
                    {
                        new
                        {
                            path = "src/encoded.cs",
                            operation = "create",
                            newContentEncoding = "base64utf8",
                            newContent = encoded,
                        },
                    },
                }),
            ],
        });
        Assert.Equal("return \"ok\";", patches.ProposedRequest.Files.Single().NewContent);

        var gzipped = GzipBase64.Encode("return \"compressed\";");
        dispatcher.Dispatch(new ContextRequest
        {
            Id = "gzip-encoded",
            Commands =
            [
                ParamCommand(CommandTypes.ProposePatch, new
                {
                    files = new[]
                    {
                        new
                        {
                            path = "src/compressed.cs",
                            operation = "create",
                            newContentEncoding = "gzipbase64utf8",
                            newContent = gzipped,
                        },
                    },
                }),
            ],
        });
        Assert.Equal("return \"compressed\";", patches.ProposedRequest.Files.Single().NewContent);

        var amend = dispatcher.Dispatch(new ContextRequest
        {
            Id = "amend",
            Commands =
            [
                ParamCommand(CommandTypes.AmendPatch, new
                {
                    patchId = "p-test",
                    baseRevision = 1,
                    files = new[]
                    {
                        new
                        {
                            path = "src/file.txt",
                            operation = "replace",
                            oldContentHash = "sha256:" + new string('1', 64),
                            newContent = "fixed",
                        },
                    },
                }),
            ],
        });

        var amendResult = Assert.Single(amend.Results!);
        Assert.Equal(ProtocolStatus.Ok, amendResult.Status);
        Assert.Equal(CommandTypes.AmendPatch, amendResult.Type);
        Assert.Equal("accepted", amendResult.Payload["patchStatus"].GetString());
        Assert.Equal("p-test", patches.AmendedRequest!.PatchId);
        Assert.Equal(1, patches.AmendedRequest.BaseRevision);

        dispatcher.Dispatch(new ContextRequest
        {
            Id = "amend-edits",
            Commands =
            [
                ParamCommand(CommandTypes.AmendPatch, new
                {
                    patchId = "p-test",
                    baseRevision = 2,
                    edits = new[]
                    {
                        new
                        {
                            path = "src/file.txt",
                            kind = "insert_after_exact",
                            anchor = "using A;\n",
                            text = "using B;\n",
                        },
                    },
                }),
            ],
        });
        var amendEdit = Assert.Single(patches.AmendedRequest.Edits);
        Assert.Equal("insert_after_exact", amendEdit.Kind);
        Assert.Equal("using A;\n", amendEdit.Anchor);
        Assert.Equal("using B;\n", amendEdit.Text);

        var validate = dispatcher.Dispatch(new ContextRequest
        {
            Id = "validate",
            Commands =
            [
                ParamCommand(CommandTypes.ValidatePatch, new
                {
                    patchId = "p-test",
                    baseRevision = 2,
                    files = new[]
                    {
                        new
                        {
                            path = "src/file.txt",
                            operation = "replace",
                            oldContentHash = "sha256:" + new string('1', 64),
                            newContent = "validated",
                        },
                    },
                    build = new { policy = "solution" },
                    tests = new { policy = "none" },
                }),
            ],
        });

        var validateResult = Assert.Single(validate.Results!);
        Assert.Equal(ProtocolStatus.Ok, validateResult.Status);
        Assert.Equal(CommandTypes.ValidatePatch, validateResult.Type);
        Assert.True(validateResult.Payload["valid"].GetBoolean());
        Assert.Equal("amend", validateResult.Payload["mode"].GetString());
        Assert.False(validateResult.Payload["applied"].GetBoolean());
        Assert.False(validateResult.Payload["diffVerified"].GetBoolean());
        Assert.Equal("validated", validateResult.Payload["build"].GetProperty("status").GetString());
        Assert.Equal("p-test", patches.ValidateRequest!.PatchId);
        Assert.Equal(2, patches.ValidateRequest.BaseRevision);
        Assert.Equal("solution", patches.ValidateRequest.Build!.Policy);

        var current = dispatcher.Dispatch(new ContextRequest
        {
            Id = "current",
            Commands = [ParamCommand(CommandTypes.CurrentPatch, new { })],
        });
        Assert.Equal("none", Assert.Single(current.Results!).Payload["patchStatus"].GetString());

        var revert = dispatcher.Dispatch(new ContextRequest
        {
            Id = "revert",
            Commands = [ParamCommand(CommandTypes.RevertPatch, new { patchId = "p-test" })],
        });
        var revertResult = Assert.Single(revert.Results!);
        Assert.Equal(ProtocolStatus.Error, revertResult.Status);
        Assert.Equal(ProtocolErrorCodes.PatchNotActive, revertResult.Error!.Code);
    }

    [Fact]
    public void Dispatch_serializes_patch_warnings_when_present()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            roslyn: null,
            session: null,
            gitStatus: null,
            patchTransactions: new WarningPatchTransactionService());

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands = [ParamCommand(CommandTypes.ProposePatch, new { edits = Array.Empty<object>() })],
        });
        var result = Assert.Single(response.Results!);

        var warning = Assert.Single(result.Payload["warnings"].EnumerateArray());
        Assert.Equal("json_formatting_changed", warning.GetProperty("code").GetString());
        Assert.Equal("JSON formatting changed.", warning.GetProperty("message").GetString());
        Assert.Equal("appsettings.json", warning.GetProperty("path").GetString());
        Assert.Equal(0, warning.GetProperty("editIndex").GetInt32());
        Assert.Equal("json_set", warning.GetProperty("kind").GetString());
    }

    [Fact]
    public void Dispatch_integration_expected_anchor_hash_valid_applies_patch()
    {
        using var temp = CreateRepo(("file.txt", "one\ntwo\nthree\n"));
        var dispatcher = RealPatchDispatcher(temp.Path);

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands =
            [
                ParamCommand(CommandTypes.ProposePatch, new
                {
                    edits = new[]
                    {
                        new
                        {
                            path = "file.txt",
                            kind = "replace_exact",
                            oldText = "two\n",
                            newText = "TWO\n",
                            expectedAnchorHash = HashText("two\n"),
                        },
                    },
                    build = new { policy = "none" },
                    tests = new { policy = "none" },
                }),
            ],
        });
        var result = Assert.Single(response.Results!);

        Assert.Equal(ProtocolStatus.Ok, result.Status);
        Assert.Equal("accepted", result.Payload["patchStatus"].GetString());
        Assert.Equal("one\nTWO\nthree\n", File.ReadAllText(Path.Combine(temp.Path, "file.txt")));
    }

    [Fact]
    public void Dispatch_integration_expected_anchor_hash_mismatch_returns_structured_error()
    {
        using var temp = CreateRepo(("file.txt", "one\ntwo\nthree\n"));
        var dispatcher = RealPatchDispatcher(temp.Path);

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands =
            [
                ParamCommand(CommandTypes.ProposePatch, new
                {
                    edits = new[]
                    {
                        new
                        {
                            path = "file.txt",
                            kind = "replace_exact",
                            oldText = "two\n",
                            newText = "TWO\n",
                            expectedAnchorHash = HashText("wrong\n"),
                        },
                    },
                }),
            ],
        });
        var error = Assert.Single(response.Results!).Error!;

        Assert.Equal(ProtocolErrorCodes.EditConflict, error.Code);
        Assert.Equal("file.txt", error.Path);
        Assert.Equal(0, error.EditIndex);
        Assert.Equal("replace_exact", error.Kind);
        Assert.Equal("expectedAnchorHash", error.HashField);
        Assert.Equal(HashText("wrong\n"), error.ExpectedHash);
        Assert.Equal(HashText("two\n"), error.ActualHash);
        Assert.Equal("oldText", error.HashTarget);
        Assert.True(new LibGit2SharpGitStatusService(temp.Path).GetStatus().IsClean);
    }

    [Fact]
    public void Dispatch_integration_malformed_expected_anchor_hash_returns_invalid_content_hash()
    {
        using var temp = CreateRepo(("file.txt", "one\ntwo\nthree\n"));
        var dispatcher = RealPatchDispatcher(temp.Path);

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands =
            [
                ParamCommand(CommandTypes.ProposePatch, new
                {
                    edits = new[]
                    {
                        new
                        {
                            path = "file.txt",
                            kind = "replace_exact",
                            oldText = "two\n",
                            newText = "TWO\n",
                            expectedAnchorHash = "sha256:XYZ",
                        },
                    },
                }),
            ],
        });
        var error = Assert.Single(response.Results!).Error!;

        Assert.Equal(ProtocolErrorCodes.InvalidContentHash, error.Code);
        Assert.Equal("expectedAnchorHash", error.HashField);
        Assert.Equal("oldText", error.HashTarget);
        Assert.Equal("sha256:<64 lowercase hex characters>", error.ExpectedFormat);
    }

    [Fact]
    public void Dispatch_integration_anchor_match_count_precedes_hash_validation()
    {
        using var temp = CreateRepo(("file.txt", "same\nsame\n"));
        var dispatcher = RealPatchDispatcher(temp.Path);

        var missing = dispatcher.Dispatch(new ContextRequest
        {
            Id = "missing",
            Commands =
            [
                ParamCommand(CommandTypes.ProposePatch, new
                {
                    edits = new[]
                    {
                        new
                        {
                            path = "file.txt",
                            kind = "replace_exact",
                            oldText = "missing\n",
                            newText = "new\n",
                            expectedAnchorHash = "sha256:XYZ",
                        },
                    },
                }),
            ],
        });
        var missingError = Assert.Single(missing.Results!).Error!;
        Assert.Equal(ProtocolErrorCodes.EditAnchorNotFound, missingError.Code);
        Assert.Equal(0, missingError.MatchCount);

        var duplicate = dispatcher.Dispatch(new ContextRequest
        {
            Id = "duplicate",
            Commands =
            [
                ParamCommand(CommandTypes.ProposePatch, new
                {
                    edits = new[]
                    {
                        new
                        {
                            path = "file.txt",
                            kind = "replace_exact",
                            oldText = "same\n",
                            newText = "new\n",
                            expectedAnchorHash = "sha256:XYZ",
                        },
                    },
                }),
            ],
        });
        var duplicateError = Assert.Single(duplicate.Results!).Error!;
        Assert.Equal(ProtocolErrorCodes.EditAnchorNotUnique, duplicateError.Code);
        Assert.Equal(2, duplicateError.MatchCount);
        Assert.Equal(2, duplicateError.Matches!.Count);
        Assert.Equal(1, duplicateError.Matches[0].Line);
        Assert.Equal(0, duplicateError.Matches[0].Column);
        Assert.Equal(2, duplicateError.Matches[1].Line);
        Assert.Equal(0, duplicateError.Matches[1].Column);
    }

    [Fact]
    public void Dispatch_integration_anchor_not_found_includes_line_ending_hint()
    {
        using var temp = CreateRepo(("file.txt", "alpha\r\nbeta\r\n"));
        var dispatcher = RealPatchDispatcher(temp.Path);

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "line-ending-hint",
            Commands =
            [
                ParamCommand(CommandTypes.ProposePatch, new
                {
                    edits = new[]
                    {
                        new
                        {
                            path = "file.txt",
                            kind = "replace_exact",
                            oldText = "missing\n",
                            newText = "new\n",
                        },
                    },
                }),
            ],
        });

        var error = Assert.Single(response.Results!).Error!;
        Assert.Equal(ProtocolErrorCodes.EditAnchorNotFound, error.Code);
        Assert.Equal("file_uses_crlf_anchor_uses_lf", error.LineEndingHint);
    }

    [Fact]
    public void Dispatch_integration_json_set_success_returns_warning()
    {
        using var temp = CreateRepo(("appsettings.json", "{\"Feature\":{\"Enabled\":false}}"));
        var dispatcher = RealPatchDispatcher(temp.Path);

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands =
            [
                ParamCommand(CommandTypes.ProposePatch, new
                {
                    edits = new[]
                    {
                        new
                        {
                            path = "appsettings.json",
                            kind = "json_set",
                            pointer = "/Feature/Enabled",
                            value = true,
                        },
                    },
                    build = new { policy = "none" },
                    tests = new { policy = "none" },
                }),
            ],
        });
        var result = Assert.Single(response.Results!);

        Assert.Equal(ProtocolStatus.Ok, result.Status);
        var warning = Assert.Single(result.Payload["warnings"].EnumerateArray());
        Assert.Equal("json_formatting_changed", warning.GetProperty("code").GetString());
        Assert.Equal("appsettings.json", warning.GetProperty("path").GetString());
        Assert.Equal(0, warning.GetProperty("editIndex").GetInt32());
        Assert.True(JsonNode.Parse(File.ReadAllText(Path.Combine(temp.Path, "appsettings.json")))!["Feature"]!["Enabled"]!.GetValue<bool>());
    }

    [Fact]
    public void Dispatch_integration_json_set_unresolved_pointer_returns_anchor_not_found()
    {
        using var temp = CreateRepo(("appsettings.json", "{\"Feature\":{\"Enabled\":false}}"));
        var dispatcher = RealPatchDispatcher(temp.Path);

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands =
            [
                ParamCommand(CommandTypes.ProposePatch, new
                {
                    edits = new[]
                    {
                        new
                        {
                            path = "appsettings.json",
                            kind = "json_set",
                            pointer = "/Feature/Missing",
                            value = true,
                        },
                    },
                }),
            ],
        });
        var error = Assert.Single(response.Results!).Error!;

        Assert.Equal(ProtocolErrorCodes.EditAnchorNotFound, error.Code);
        Assert.Equal("json_set", error.Kind);
        Assert.Equal(0, error.MatchCount);
        Assert.True(new LibGit2SharpGitStatusService(temp.Path).GetStatus().IsClean);
    }

    [Fact]
    public void Dispatch_integration_create_over_existing_does_not_delete_existing_file()
    {
        using var temp = CreateRepo(("docs/chat-prompt.md", "original\n"));
        var dispatcher = RealPatchDispatcher(temp.Path);

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands =
            [
                ParamCommand(CommandTypes.ProposePatch, new
                {
                    files = new[]
                    {
                        new
                        {
                            path = "docs/chat-prompt.md",
                            operation = "create",
                            newContent = "MUST-NOT-APPLY\n",
                        },
                    },
                    build = new { policy = "none" },
                    tests = new { policy = "none" },
                }),
            ],
        });
        var error = Assert.Single(response.Results!).Error!;

        Assert.Equal("file_exists", error.Code);
        Assert.Equal("original\n", File.ReadAllText(Path.Combine(temp.Path, "docs/chat-prompt.md")));
        Assert.True(new LibGit2SharpGitStatusService(temp.Path).GetStatus().IsClean);
    }

    [Fact]
    public void Dispatch_integration_invalid_policy_persists_needs_revision()
    {
        using var temp = CreateRepo(("ContextMessenger.slnx", "<Solution />"));
        var dispatcher = RealPatchDispatcher(temp.Path);

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands =
            [
                ParamCommand(CommandTypes.ProposePatch, new
                {
                    files = new[]
                    {
                        new
                        {
                            path = "policy.txt",
                            operation = "create",
                            newContent = "policy\n",
                        },
                    },
                    build = new { policy = "none" },
                    tests = new { policy = "filter", filter = "FullyQualifiedName~Anything" },
                }),
            ],
        });
        var result = Assert.Single(response.Results!);

        Assert.Equal(ProtocolStatus.Ok, result.Status);
        Assert.Equal("needs_revision", result.Payload["patchStatus"].GetString());
        Assert.Equal("tests", result.Payload["lastFailureStage"].GetString());
        var diagnostic = Assert.Single(result.Payload["tests"].GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("invalid_patch_policy", diagnostic.GetProperty("code").GetString());
        Assert.False(new LibGit2SharpGitStatusService(temp.Path).GetStatus().IsClean);
    }

    [Fact]
    public void Dispatch_integration_amend_apply_failure_restores_pre_amend_state()
    {
        using var temp = CreateRepo(("existing.txt", "existing\n"));
        var dispatcher = RealPatchDispatcher(temp.Path);

        var propose = dispatcher.Dispatch(new ContextRequest
        {
            Id = "propose",
            Commands =
            [
                ParamCommand(CommandTypes.ProposePatch, new
                {
                    files = new object[]
                    {
                        new
                        {
                            path = "scratch.txt",
                            operation = "create",
                            newContent = "before\n",
                        },
                    },
                    build = new { policy = "none" },
                    tests = new { policy = "filter", filter = "FullyQualifiedName~Anything" },
                }),
            ],
        });
        var proposed = Assert.Single(propose.Results!);
        var patchId = proposed.Payload["patchId"].GetString()!;
        Assert.Equal("needs_revision", proposed.Payload["patchStatus"].GetString());

        var amend = dispatcher.Dispatch(new ContextRequest
        {
            Id = "amend",
            Commands =
            [
                ParamCommand(CommandTypes.AmendPatch, new
                {
                    patchId,
                    baseRevision = 1,
                    files = new object[]
                    {
                        new
                        {
                            path = "scratch.txt",
                            operation = "replace",
                            oldContentHash = HashText("before\n"),
                            newContent = "temporary\n",
                        },
                        new
                        {
                            path = "existing.txt",
                            operation = "create",
                            newContent = "MUST-NOT-APPLY\n",
                        },
                    },
                }),
            ],
        });
        var error = Assert.Single(amend.Results!).Error!;

        Assert.Equal("file_exists", error.Code);
        Assert.Equal("before\n", File.ReadAllText(Path.Combine(temp.Path, "scratch.txt")));
        Assert.Equal("existing\n", File.ReadAllText(Path.Combine(temp.Path, "existing.txt")));
    }

    [Fact]
    public void Dispatch_integration_semantic_error_shape_is_serialized()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            roslyn: null,
            session: null,
            gitStatus: null,
            patchTransactions: new ThrowingPatchTransactionService(
                new PatchValidationException(
                    ProtocolErrorCodes.SemanticSpanHashMismatch,
                    "oldSourceHash mismatch",
                    path: "Parser.cs",
                    editIndex: 0,
                    kind: "replace_symbol_source",
                    hashField: "oldSourceHash",
                    expectedHash: "sha256:" + new string('0', 64),
                    actualHash: "sha256:" + new string('1', 64),
                    hashTarget: "source")));

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands = [ParamCommand(CommandTypes.ProposePatch, new { edits = Array.Empty<object>() })],
        });
        var error = Assert.Single(response.Results!).Error!;

        Assert.Equal(ProtocolErrorCodes.SemanticSpanHashMismatch, error.Code);
        Assert.Equal("Parser.cs", error.Path);
        Assert.Equal("replace_symbol_source", error.Kind);
        Assert.Equal("oldSourceHash", error.HashField);
        Assert.Equal("source", error.HashTarget);
    }

    [Fact]
    public void Dispatch_maps_patch_validation_errors()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            roslyn: null,
            session: null,
            gitStatus: null,
            patchTransactions: new ThrowingPatchTransactionService(
                new PatchValidationException(ProtocolErrorCodes.DirtyWorkingTree, "dirty")));

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands = [ParamCommand(CommandTypes.CurrentPatch, new { })],
        });
        var result = Assert.Single(response.Results!);

        Assert.Equal(ProtocolStatus.Error, result.Status);
        Assert.Equal(ProtocolErrorCodes.DirtyWorkingTree, result.Error!.Code);
    }

    [Fact]
    public void Dispatch_maps_patch_edit_validation_error_details()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            roslyn: null,
            session: null,
            gitStatus: null,
            patchTransactions: new ThrowingPatchTransactionService(
                new PatchValidationException(
                    ProtocolErrorCodes.EditAnchorNotUnique,
                    "not unique",
                    path: "src/file.txt",
                    editIndex: 2,
                    kind: "replace_exact",
                    matchCount: 3,
                    hashField: "expectedAnchorHash",
                    expectedHash: "sha256:" + new string('0', 64),
                    actualHash: "sha256:" + new string('1', 64),
                    hashTarget: "oldText",
                    expectedFormat: "sha256:<64 lowercase hex characters>",
                    matches:
                    [
                        new PatchEditMatchLocation(2, 0),
                        new PatchEditMatchLocation(6, 4),
                    ])));

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "abc",
            Commands = [ParamCommand(CommandTypes.ProposePatch, new { edits = Array.Empty<object>() })],
        });
        var result = Assert.Single(response.Results!);

        Assert.Equal(ProtocolStatus.Error, result.Status);
        Assert.Equal(ProtocolErrorCodes.EditAnchorNotUnique, result.Error!.Code);
        Assert.Equal("src/file.txt", result.Error.Path);
        Assert.Equal(2, result.Error.EditIndex);
        Assert.Equal("replace_exact", result.Error.Kind);
        Assert.Equal(3, result.Error.MatchCount);
        Assert.Equal("expectedAnchorHash", result.Error.HashField);
        Assert.Equal("sha256:" + new string('0', 64), result.Error.ExpectedHash);
        Assert.Equal("sha256:" + new string('1', 64), result.Error.ActualHash);
        Assert.Equal("oldText", result.Error.HashTarget);
        Assert.Equal("sha256:<64 lowercase hex characters>", result.Error.ExpectedFormat);
        Assert.Collection(result.Error.Matches!,
            match =>
            {
                Assert.Equal(2, match.Line);
                Assert.Equal(0, match.Column);
            },
            match =>
            {
                Assert.Equal(6, match.Line);
                Assert.Equal(4, match.Column);
            });
    }

    [Fact]
    public void Dispatch_rejects_invalid_gzip_encoded_patch_content()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            roslyn: null,
            session: null,
            gitStatus: null,
            patchTransactions: new FakePatchTransactionService());

        var response = dispatcher.Dispatch(new ContextRequest
        {
            Id = "bad-gzip",
            Commands =
            [
                ParamCommand(CommandTypes.ProposePatch, new
                {
                    files = new[]
                    {
                        new
                        {
                            path = "src/bad.cs",
                            operation = "create",
                            newContentEncoding = "gzipbase64utf8",
                            newContent = Convert.ToBase64String([1, 2, 3, 4]),
                        },
                    },
                }),
            ],
        });

        var result = Assert.Single(response.Results!);
        Assert.Equal(ProtocolStatus.Error, result.Status);
        Assert.Equal(ProtocolErrorCodes.InvalidParameters, result.Error!.Code);
        Assert.Contains("gzip+base64", result.Error.Message);
    }

    [Fact]
    public void Dispatch_executes_search_text_and_returns_match_array()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("src/foo.cs", "the needle is here");
        temp.CreateFile("src/bar.cs", "no match");
        var dispatcher = CommandDispatcher.ForFileSystem(new FileSystemContextService(temp.Path));

        var request = new ContextRequest
        {
            Id = "abc",
            Commands = [ParamCommand(CommandTypes.SearchText, new { pattern = "needle" })],
        };
        var response = dispatcher.Dispatch(request);
        var result = Assert.Single(response.Results!);

        Assert.Equal(ProtocolStatus.Ok, result.Status);
        Assert.Equal(1, result.Payload["matchCount"].GetInt32());
        var matches = result.Payload["matches"].EnumerateArray().ToArray();
        Assert.Single(matches);
        Assert.Equal("src/foo.cs", matches[0].GetProperty("path").GetString());
    }

    [Fact]
    public void Dispatch_executes_list_files_and_returns_paths()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("a.cs", "x");
        temp.CreateFile("src/b.cs", "x");
        var dispatcher = CommandDispatcher.ForFileSystem(new FileSystemContextService(temp.Path));

        var request = new ContextRequest
        {
            Id = "abc",
            Commands = [ParamCommand(CommandTypes.ListFiles, new { })],
        };
        var response = dispatcher.Dispatch(request);
        var result = Assert.Single(response.Results!);

        Assert.Equal(ProtocolStatus.Ok, result.Status);
        Assert.Equal(2, result.Payload["fileCount"].GetInt32());
        var files = result.Payload["files"].EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("a.cs", files);
        Assert.Contains("src/b.cs", files);
    }

    [Fact]
    public void Dispatch_executes_project_info_and_returns_inventory()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("ContextMessenger.slnx", "<Solution />");
        temp.CreateFile(
            "src/ContextMessenger.Protocol/ContextMessenger.Protocol.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\ContextMessenger.Core\ContextMessenger.Core.csproj" />
              </ItemGroup>
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        var dispatcher = CommandDispatcher.ForFileSystem(new FileSystemContextService(temp.Path));

        var request = new ContextRequest
        {
            Id = "abc",
            Commands = [ParamCommand(CommandTypes.ProjectInfo, new { })],
        };
        var response = dispatcher.Dispatch(request);
        var result = Assert.Single(response.Results!);

        Assert.Equal(ProtocolStatus.Ok, result.Status);
        Assert.Equal(CommandTypes.ProjectInfo, result.Type);
        Assert.Equal(".", result.Payload["rootPath"].GetString());
        Assert.Equal("ContextMessenger.slnx", Assert.Single(result.Payload["solutionFiles"].EnumerateArray()).GetString());
        var project = Assert.Single(result.Payload["projectFiles"].EnumerateArray());
        Assert.Equal("ContextMessenger.Protocol", project.GetProperty("name").GetString());
        Assert.Equal("src/ContextMessenger.Protocol/ContextMessenger.Protocol.csproj", project.GetProperty("path").GetString());
        Assert.Equal("net10.0", project.GetProperty("targetFramework").GetString());
        Assert.False(project.TryGetProperty("targetFrameworks", out _));
        Assert.Equal(
            "src/ContextMessenger.Core/ContextMessenger.Core.csproj",
            Assert.Single(project.GetProperty("projectReferences").EnumerateArray()).GetString());
        Assert.Empty(result.Payload["testProjects"].EnumerateArray());
    }

    [Fact]
    public void Dispatch_executes_document_symbols_and_returns_file_structure()
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
                }
            }
            """);
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            new FakeRoslynNavigationService());

        var request = new ContextRequest
        {
            Id = "abc",
            Commands = [ParamCommand(CommandTypes.DocumentSymbols, new { path = "src/Parser.cs" })],
        };
        var response = dispatcher.Dispatch(request);
        var result = Assert.Single(response.Results!);

        Assert.True(
            result.Status == ProtocolStatus.Ok,
            result.Error is null ? "No error payload." : $"{result.Error.Code}: {result.Error.Message}");
        Assert.Equal(CommandTypes.DocumentSymbols, result.Type);
        Assert.Equal("src/Parser.cs", result.Payload["path"].GetString());
        var parser = Assert.Single(result.Payload["symbols"].EnumerateArray());
        Assert.Equal("Parser", parser.GetProperty("name").GetString());
        Assert.Equal("class", parser.GetProperty("kind").GetString());
        var parse = Assert.Single(parser.GetProperty("children").EnumerateArray());
        Assert.Equal("Parse", parse.GetProperty("name").GetString());
        Assert.Equal("method", parse.GetProperty("kind").GetString());
    }

    [Fact]
    public void Dispatch_returns_unsupported_file_type_for_document_symbols_on_non_csharp_file()
    {
        using var temp = new TempDirectory();
        var roslyn = new ThrowingRoslynNavigationService(
            new NotSupportedException("document_symbols supports only .cs and .csx files: docs/readme.md"));
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            roslyn);

        var request = new ContextRequest
        {
            Id = "abc",
            Commands = [ParamCommand(CommandTypes.DocumentSymbols, new { path = "docs/readme.md" })],
        };
        var response = dispatcher.Dispatch(request);
        var result = Assert.Single(response.Results!);

        Assert.Equal(ProtocolStatus.Error, result.Status);
        Assert.Equal(ProtocolErrorCodes.UnsupportedFileType, result.Error!.Code);
    }

    [Fact]
    public void Dispatch_executes_find_implementations()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            new FakeRoslynNavigationService());

        var request = new ContextRequest
        {
            Id = "abc",
            Commands =
            [
                ParamCommand(CommandTypes.FindImplementations, new
                {
                    symbolId = "T:Demo.IParser",
                    maxResults = 10,
                }),
            ],
        };
        var response = dispatcher.Dispatch(request);
        var result = Assert.Single(response.Results!);

        Assert.Equal(ProtocolStatus.Ok, result.Status);
        Assert.Equal(CommandTypes.FindImplementations, result.Type);
        Assert.Equal("sha256:test", result.Payload["workspaceVersion"].GetString());
        Assert.Equal("IParser", result.Payload["symbol"].GetProperty("name").GetString());
        var implementation = Assert.Single(result.Payload["implementations"].EnumerateArray());
        Assert.Equal("Parser", implementation.GetProperty("name").GetString());
    }

    [Fact]
    public void Dispatch_executes_find_callers()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            new FakeRoslynNavigationService());

        var request = new ContextRequest
        {
            Id = "abc",
            Commands =
            [
                ParamCommand(CommandTypes.FindCallers, new
                {
                    symbolId = "M:Demo.Parser.Parse(System.String)",
                    maxResults = 10,
                }),
            ],
        };
        var response = dispatcher.Dispatch(request);
        var result = Assert.Single(response.Results!);

        Assert.Equal(ProtocolStatus.Ok, result.Status);
        Assert.Equal(CommandTypes.FindCallers, result.Type);
        Assert.Equal("Parse", result.Payload["symbol"].GetProperty("name").GetString());
        var caller = Assert.Single(result.Payload["callers"].EnumerateArray());
        Assert.Equal("tests/ParserTests.cs", caller.GetProperty("path").GetString());
        Assert.Equal("Parser.Parse(text);", caller.GetProperty("text").GetString());
    }

    [Fact]
    public void Dispatch_binds_find_references_kinds_filter()
    {
        using var temp = new TempDirectory();
        var roslyn = new CapturingRoslynNavigationService();
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            roslyn);

        var request = new ContextRequest
        {
            Id = "abc",
            Commands =
            [
                ParamCommand(CommandTypes.FindReferences, new
                {
                    symbolId = "M:Demo.Parser.Parse(System.String)",
                    kinds = new[] { "call" },
                    maxResults = 25,
                }),
            ],
        };

        dispatcher.Dispatch(request);

        Assert.NotNull(roslyn.FindReferencesQuery);
        Assert.Equal(["call"], roslyn.FindReferencesQuery.Kinds);
        Assert.Equal(25, roslyn.FindReferencesQuery.MaxResults);
    }

    [Fact]
    public void Dispatch_executes_find_derived_types()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            new FakeRoslynNavigationService());

        var request = new ContextRequest
        {
            Id = "abc",
            Commands =
            [
                ParamCommand(CommandTypes.FindDerivedTypes, new
                {
                    symbolId = "T:Demo.ParserBase",
                    transitive = true,
                    maxResults = 10,
                }),
            ],
        };

        var response = dispatcher.Dispatch(request);
        var result = Assert.Single(response.Results!);

        Assert.Equal(ProtocolStatus.Ok, result.Status);
        Assert.Equal(CommandTypes.FindDerivedTypes, result.Type);
        Assert.Equal("ParserBase", result.Payload["symbol"].GetProperty("name").GetString());
        var derived = Assert.Single(result.Payload["derivedTypes"].EnumerateArray());
        Assert.Equal("Parser", derived.GetProperty("name").GetString());
    }

    [Fact]
    public void Dispatch_executes_get_symbol_info()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            new FakeRoslynNavigationService());

        var request = new ContextRequest
        {
            Id = "abc",
            Commands =
            [
                ParamCommand(CommandTypes.GetSymbolInfo, new
                {
                    symbolId = "T:Demo.Parser",
                }),
            ],
        };

        var response = dispatcher.Dispatch(request);
        var result = Assert.Single(response.Results!);

        Assert.Equal(ProtocolStatus.Ok, result.Status);
        Assert.Equal(CommandTypes.GetSymbolInfo, result.Type);
        Assert.Equal("Parser", result.Payload["symbol"].GetProperty("name").GetString());
        Assert.Equal("System.IDisposable", Assert.Single(result.Payload["implementedInterfaces"].EnumerateArray()).GetString());
        Assert.Equal("void", result.Payload["returnType"].GetString());
        Assert.Equal("text", Assert.Single(result.Payload["parameters"].EnumerateArray()).GetProperty("name").GetString());
    }

    [Fact]
    public void Dispatch_executes_find_overrides()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            new FakeRoslynNavigationService());

        var request = new ContextRequest
        {
            Id = "abc",
            Commands =
            [
                ParamCommand(CommandTypes.FindOverrides, new
                {
                    path = "src/ParserBase.cs",
                    line = 5,
                    column = 35,
                    maxResults = 10,
                }),
            ],
        };

        var response = dispatcher.Dispatch(request);
        var result = Assert.Single(response.Results!);

        Assert.Equal(ProtocolStatus.Ok, result.Status);
        Assert.Equal(CommandTypes.FindOverrides, result.Type);
        Assert.Equal("sha256:test", result.Payload["workspaceVersion"].GetString());
        Assert.Equal("Parse", result.Payload["symbol"].GetProperty("name").GetString());
        var overrideSymbol = Assert.Single(result.Payload["overrides"].EnumerateArray());
        Assert.Equal("Parser", overrideSymbol.GetProperty("containingType").GetString());
    }

    [Fact]
    public void Dispatch_returns_symbol_not_found_for_unresolved_symbol_id()
    {
        using var temp = new TempDirectory();
        var roslyn = new ThrowingRoslynNavigationService(new SymbolNotFoundException("T:Demo.Missing"));
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            roslyn);

        var request = new ContextRequest
        {
            Id = "abc",
            Commands =
            [
                ParamCommand(CommandTypes.GetSymbolInfo, new
                {
                    symbolId = "T:Demo.Missing",
                }),
            ],
        };

        var response = dispatcher.Dispatch(request);
        var result = Assert.Single(response.Results!);

        Assert.Equal(ProtocolStatus.Error, result.Status);
        Assert.Equal(ProtocolErrorCodes.SymbolNotFound, result.Error!.Code);
    }

    [Fact]
    public void Dispatch_isolates_per_command_failures()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("src/exists.cs", "x");
        var dispatcher = CommandDispatcher.ForFileSystem(new FileSystemContextService(temp.Path));

        var request = new ContextRequest
        {
            Id = "abc",
            Commands = [
                ParamCommand(CommandTypes.ReadFile, new { path = "src/exists.cs" }),
                ParamCommand(CommandTypes.ReadFile, new { path = "src/missing.cs" }),
                ParamCommand(CommandTypes.Tree, new { path = ".", depth = 1 }),
            ],
        };

        var response = dispatcher.Dispatch(request);

        Assert.Equal(ProtocolStatus.Ok, response.Status); // top-level still ok
        var results = response.Results!;
        Assert.Equal(3, results.Count);

        Assert.Equal(ProtocolStatus.Ok, results[0].Status);
        Assert.Equal(0, results[0].CommandIndex);

        Assert.Equal(ProtocolStatus.Error, results[1].Status);
        Assert.Equal(1, results[1].CommandIndex);
        Assert.Equal(ProtocolErrorCodes.FileNotFound, results[1].Error!.Code);

        Assert.Equal(ProtocolStatus.Ok, results[2].Status);
        Assert.Equal(2, results[2].CommandIndex);
    }

    [Fact]
    public void Dispatch_returns_unsupported_command_for_unknown_type()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForFileSystem(new FileSystemContextService(temp.Path));

        var request = new ContextRequest
        {
            Id = "abc",
            Commands = [new ContextCommand { Type = "unknown_command" }],
        };
        var response = dispatcher.Dispatch(request);
        var result = Assert.Single(response.Results!);

        Assert.Equal(ProtocolStatus.Error, result.Status);
        Assert.Equal(ProtocolErrorCodes.UnsupportedCommand, result.Error!.Code);
    }

    [Fact]
    public void Dispatch_catches_path_outside_sandbox_with_specific_code()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForFileSystem(new FileSystemContextService(temp.Path));

        var request = new ContextRequest
        {
            Id = "abc",
            Commands = [ParamCommand(CommandTypes.ReadFile, new { path = "../escape.cs" })],
        };
        var response = dispatcher.Dispatch(request);
        var result = Assert.Single(response.Results!);

        Assert.Equal(ProtocolStatus.Error, result.Status);
        Assert.Equal(ProtocolErrorCodes.PathOutsideSandbox, result.Error!.Code);
    }

    [Fact]
    public void Dispatch_returns_invalid_parameters_when_required_field_missing()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForFileSystem(new FileSystemContextService(temp.Path));

        // read_file without path
        var request = new ContextRequest
        {
            Id = "abc",
            Commands = [new ContextCommand { Type = CommandTypes.ReadFile }],
        };
        var response = dispatcher.Dispatch(request);
        var result = Assert.Single(response.Results!);

        Assert.Equal(ProtocolStatus.Error, result.Status);
        Assert.Equal(ProtocolErrorCodes.InvalidParameters, result.Error!.Code);
    }

    [Fact]
    public void ProcessRawInput_handles_full_round_trip()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("src/hello.txt", "hello world");
        var dispatcher = CommandDispatcher.ForFileSystem(new FileSystemContextService(temp.Path));

        var input = """
            {
              "version": "1.0",
              "id": "11111111-1111-1111-1111-111111111111",
              "commands": [
                { "type": "read_file", "path": "src/hello.txt" }
              ]
            }
            """;

        var output = dispatcher.ProcessRequests([input]);

        Assert.StartsWith(ProtocolDelimiters.BeginResponse, output);
        Assert.EndsWith(ProtocolDelimiters.EndResponse, output);
        Assert.Contains("\"hello world\"", output);
        Assert.Contains("\"11111111-1111-1111-1111-111111111111\"", output);
    }

    [Fact]
    public void ProcessRawInput_returns_top_level_error_when_parse_fails()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForFileSystem(new FileSystemContextService(temp.Path));

        var output = dispatcher.ProcessRequests(["just some chatter, no delimiters"]);

        Assert.Contains("\"status\": \"error\"", output);
        Assert.Contains($"\"code\": \"{ProtocolErrorCodes.InvalidJson}\"", output);
        Assert.Contains("\"id\": \"unknown\"", output);
    }

    [Fact]
    public void ProcessRawInput_echoes_id_when_validation_fails_after_parse()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForFileSystem(new FileSystemContextService(temp.Path));

        var input = """
            { "version": "1.0", "id": "abc-123", "commands": [] }
            """;

        var output = dispatcher.ProcessRequests([input]);

        Assert.Contains("\"status\": \"error\"", output);
        Assert.Contains($"\"code\": \"{ProtocolErrorCodes.EmptyCommandSet}\"", output);
        Assert.Contains("\"id\": \"abc-123\"", output);
    }

    [Fact]
    public void ProcessRawInput_echoes_compact_guid_id_when_parse_fails()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForFileSystem(new FileSystemContextService(temp.Path));

        var output = dispatcher.ProcessRequests(["{\"id\":\"a82b14e6-7c3f-49d0-bf21-5d6e8a0c1f47\",\"commands\":["]);

        Assert.Contains($"\"code\": \"{ProtocolErrorCodes.InvalidJson}\"", output);
        Assert.Contains("\"id\": \"a82b14e6-7c3f-49d0-bf21-5d6e8a0c1f47\"", output);
    }

    [Fact]
    public void ProcessRawInput_echoes_guid_id_when_version_precedes_id_and_parse_fails()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForFileSystem(new FileSystemContextService(temp.Path));

        var output = dispatcher.ProcessRequests(["{\"version\":\"1.0\",\"id\":\"a82b14e6-7c3f-49d0-bf21-5d6e8a0c1f47\",\"commands\":["]);

        Assert.Contains($"\"code\": \"{ProtocolErrorCodes.InvalidJson}\"", output);
        Assert.Contains("\"id\": \"a82b14e6-7c3f-49d0-bf21-5d6e8a0c1f47\"", output);
    }

    [Fact]
    public void ProcessRawInput_returns_hint_when_newContent_quotes_break_json()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForFileSystem(new FileSystemContextService(temp.Path));

        var output = dispatcher.ProcessRequests([
            """
            {"version":"1.0","id":"a82b14e6-7c3f-49d0-bf21-5d6e8a0c1f47","commands":[{"type":"propose_patch","files":[{"path":"x.cs","operation":"create","newContent":"return "ok";"}]}]}
            """]);

        Assert.Contains($"\"code\": \"{ProtocolErrorCodes.InvalidJson}\"", output);
        Assert.Contains("\"id\": \"a82b14e6-7c3f-49d0-bf21-5d6e8a0c1f47\"", output);
        Assert.Contains("newContentEncoding", output);
        Assert.Contains("base64utf8", output);
        Assert.Contains("gzipbase64utf8", output);
    }

    [Fact]
    public void Dispatch_silently_drops_duplicate_id_within_batch()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForFileSystem(new FileSystemContextService(temp.Path));

        var responses = dispatcher.Dispatch([
                new ContextRequest { Id = "abc", Commands = [new ContextCommand { Type = CommandTypes.Tree }] },
                new ContextRequest { Id = "abc", Commands = [new ContextCommand { Type = CommandTypes.Tree }] },
            ]);

        Assert.Single(responses);
        Assert.Equal("abc", responses[0].Id);
    }

    [Fact]
    public void Dispatch_silently_drops_id_present_in_cache()
    {
        using var temp = new TempDirectory();
        var cache = new InMemoryRequestIdCache();
        cache.TryAdd("abc");
        var dispatcher = CommandDispatcher.ForFileSystem(new FileSystemContextService(temp.Path), cache);

        var responses = dispatcher.Dispatch([new ContextRequest { Id = "abc", Commands = [new ContextCommand { Type = CommandTypes.Tree }] }]);

        Assert.Empty(responses);
    }

    [Fact]
    public void ProcessRawInput_returns_empty_string_when_single_request_is_duplicate()
    {
        using var temp = new TempDirectory();
        var cache = new InMemoryRequestIdCache();
        cache.TryAdd("abc");
        var dispatcher = CommandDispatcher.ForFileSystem(new FileSystemContextService(temp.Path), cache);
        var input = """
            { "id": "abc", "commands": [{ "type": "tree" }] }
            """;

        var output = dispatcher.ProcessRequests([input]);

        Assert.Equal("", output);
    }

    [Fact]
    public void Dispatch_processes_unique_ids_when_batch_contains_some_duplicates()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForFileSystem(new FileSystemContextService(temp.Path));

        var responses = dispatcher.Dispatch([
                new ContextRequest { Id = "abc", Commands = [new ContextCommand { Type = CommandTypes.Tree }] },
                new ContextRequest { Id = "abc", Commands = [new ContextCommand { Type = CommandTypes.Tree }] },
                new ContextRequest { Id = "def", Commands = [new ContextCommand { Type = CommandTypes.Tree }] },
            ]);

        Assert.Equal(["abc", "def"], responses.Select(r => r.Id).ToArray());
    }

    [Fact]
    public void ProcessRawInput_handles_multi_request_batch()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("a.txt", "hello");
        var dispatcher = CommandDispatcher.ForFileSystem(new FileSystemContextService(temp.Path));
        var input = """
            [
              { "id": "11111111-1111-1111-1111-111111111111", "commands": [{ "type": "tree", "path": "." }] },
              { "id": "22222222-2222-2222-2222-222222222222", "commands": [{ "type": "read_file", "path": "a.txt" }] }
            ]
            """;

        var output = dispatcher.ProcessRequests([input]);

        Assert.Contains("\n[\n", output.Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Contains("11111111-1111-1111-1111-111111111111", output);
        Assert.Contains("22222222-2222-2222-2222-222222222222", output);
        Assert.Contains("hello", output);
    }

    [Fact]
    public void ProcessRawInput_aggregates_multiple_request_bodies_into_single_response_array()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("a.txt", "hello");
        var dispatcher = CommandDispatcher.ForFileSystem(new FileSystemContextService(temp.Path));

        var output = dispatcher.ProcessRequests([
            """
            { "version": "1.0", "id": "11111111-1111-1111-1111-111111111111", "commands": [{ "type": "tree", "path": "." }] }
            """,
            """
            { "version": "1.0", "id": "22222222-2222-2222-2222-222222222222", "commands": [{ "type": "read_file", "path": "a.txt" }] }
            """]);

        var normalized = output.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.StartsWith(ProtocolDelimiters.BeginResponse, output);
        Assert.EndsWith(ProtocolDelimiters.EndResponse, output);
        Assert.Single(IndexesOf(normalized, ProtocolDelimiters.BeginResponse));
        Assert.Contains("\n[\n", normalized);
        Assert.Contains("11111111-1111-1111-1111-111111111111", output);
        Assert.Contains("22222222-2222-2222-2222-222222222222", output);
        Assert.Contains("hello", output);
    }

    [Fact]
    public void ProcessRawInput_processes_four_separate_request_bodies()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            roslyn: null,
            session: new FakeContextSession(temp.Path),
            gitStatus: new FakeGitStatusService());

        var output = dispatcher.ProcessRequests([
            """
            {
            "version": "1.0",
            "id": "6f80c86d-cb1f-4e95-93c4-e9f7ba28b5c2",
            "commands": [
            {
            "type": "current_context"
            }
            ]
            }
            """,
            """
            {
            "version": "1.0",
            "id": "3c813241-0b62-4fbd-945d-ef6e09f4d0df",
            "commands": [
            {
            "type": "git_status"
            }
            ]
            }
            """,
            """
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
            """,
            """
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
            """]);

        Assert.Contains("6f80c86d-cb1f-4e95-93c4-e9f7ba28b5c2", output);
        Assert.Contains("3c813241-0b62-4fbd-945d-ef6e09f4d0df", output);
        Assert.Contains("f86e1bda-99a1-4f16-a812-b5aad46adf3b9", output);
        Assert.Contains("c0b369fa-5a53-4dd1-8991-3157a327877e", output);
        using var doc = JsonDocument.Parse(ResponseBody(output));
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(4, doc.RootElement.GetArrayLength());
        Assert.All(doc.RootElement.EnumerateArray(), response =>
            Assert.Equal(ProtocolStatus.Ok, response.GetProperty("status").GetString()));
    }

    [Fact]
    public void ProcessRawInput_includes_error_response_when_one_of_multiple_bodies_is_invalid()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("a.txt", "hello");
        var dispatcher = CommandDispatcher.ForFileSystem(new FileSystemContextService(temp.Path));

        var output = dispatcher.ProcessRequests([
            """
            { "version": "1.0", "id": "11111111-1111-1111-1111-111111111111", "commands": [{ "type": "read_file", "path": "a.txt" }] }
            """,
            """
            { "version": "1.0", "id": "22222222-2222-2222-2222-222222222222", "commands": [] }
            """]);

        Assert.Contains("11111111-1111-1111-1111-111111111111", output);
        Assert.Contains("22222222-2222-2222-2222-222222222222", output);
        Assert.Contains($"\"code\": \"{ProtocolErrorCodes.EmptyCommandSet}\"", output);
        Assert.Contains("hello", output);
    }

    private static IReadOnlyList<int> IndexesOf(string text, string value)
    {
        var indexes = new List<int>();
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            indexes.Add(index);
            index += value.Length;
        }

        return indexes;
    }

    private static string ResponseBody(string output)
    {
        var body = output.Trim();
        if (body.StartsWith(ProtocolDelimiters.BeginResponse, StringComparison.Ordinal))
            body = body[ProtocolDelimiters.BeginResponse.Length..].Trim();
        if (body.EndsWith(ProtocolDelimiters.EndResponse, StringComparison.Ordinal))
            body = body[..^ProtocolDelimiters.EndResponse.Length].Trim();
        return body;
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

    // ---- Option A: structured patch outcome extraction from ProcessRequestsDetailed ----

    [Fact]
    public void ProcessRequestsDetailed_surfaces_accepted_propose_outcome()
    {
        using var temp = CreateRepo();
        var dispatcher = RealPatchDispatcher(temp.Path);

        var result = dispatcher.ProcessRequestsDetailed([ProposeBody(
            "a82b14e6-7c3f-49d0-bf21-5d6e8a0c1f47", "new.txt", "hello")]);

        var outcome = Assert.Single(result.PatchOutcomes);
        Assert.Equal(CommandTypes.ProposePatch, outcome.CommandType);
        Assert.Equal("accepted", outcome.PatchStatus);
        Assert.NotNull(outcome.PatchId);
        Assert.Equal(1, outcome.Revision);
        Assert.Contains("BEGIN_RESPONSE", result.ResponseText);
    }

    [Fact]
    public void ProcessRequestsDetailed_surfaces_comment_replies_on_amend_outcome()
    {
        using var temp = new TempDirectory();
        var patches = new FakePatchTransactionService();
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            roslyn: null,
            session: null,
            gitStatus: null,
            patchTransactions: patches);

        var body = JsonSerializer.Serialize(new
        {
            version = "1.0",
            id = "a82b14e6-7c3f-49d0-bf21-5d6e8a0c1f47",
            commands = new object[]
            {
                new
                {
                    type = "amend_patch",
                    patchId = "p-test",
                    baseRevision = 1,
                    files = new object[]
                    {
                        new { path = "src/file.txt", operation = "replace", oldContentHash = "sha256:" + new string('1', 64), newContent = "fixed" },
                    },
                    commentReplies = new object[] { new { id = "c-1", reply = "fixed it", path = "src/file.txt", line = 2 } },
                },
            },
        });

        var result = dispatcher.ProcessRequestsDetailed([body]);

        var outcome = Assert.Single(result.PatchOutcomes);
        Assert.Equal(CommandTypes.AmendPatch, outcome.CommandType);
        var reply = Assert.Single(outcome.CommentReplies);
        Assert.Equal("c-1", reply.Id);
        Assert.Equal("fixed it", reply.Reply);
        Assert.Equal("src/file.txt", reply.Path);
        Assert.Equal(2, reply.Line);
    }

    [Fact]
    public void ProcessRequestsDetailed_returns_no_outcomes_for_non_patch_batch()
    {
        using var temp = CreateRepo(("file.txt", "x"));
        var dispatcher = RealPatchDispatcher(temp.Path);

        var body = JsonSerializer.Serialize(new
        {
            version = "1.0",
            id = "b1c2d3e4-0000-49d0-bf21-5d6e8a0c1f47",
            commands = new object[] { new { type = "tree", path = ".", depth = 1 } },
        });

        var result = dispatcher.ProcessRequestsDetailed([body]);

        Assert.Empty(result.PatchOutcomes);
        Assert.Contains("BEGIN_RESPONSE", result.ResponseText);
    }

    [Fact]
    public void ProcessRequestsDetailed_ignores_failed_patch_command()
    {
        using var temp = CreateRepo();
        var dispatcher = RealPatchDispatcher(temp.Path);

        // propose_patch with neither files nor edits -> invalid_parameters error, no patchStatus.
        var body = JsonSerializer.Serialize(new
        {
            version = "1.0",
            id = "c1c2d3e4-0000-49d0-bf21-5d6e8a0c1f47",
            commands = new object[] { new { type = "propose_patch" } },
        });

        var result = dispatcher.ProcessRequestsDetailed([body]);

        Assert.Empty(result.PatchOutcomes);
        Assert.Contains("invalid_parameters", result.ResponseText);
    }

    [Fact]
    public void ProcessRequestsDetailed_surfaces_policy_failure_as_build_error_for_review_ui()
    {
        using var temp = new TempDirectory();
        var dispatcher = CommandDispatcher.ForServices(
            new FileSystemContextService(temp.Path),
            roslyn: null,
            session: null,
            gitStatus: null,
            patchTransactions: new PolicyFailurePatchTransactionService());

        var body = JsonSerializer.Serialize(new
        {
            version = "1.0",
            id = "d1c2d3e4-0000-49d0-bf21-5d6e8a0c1f47",
            commands = new object[]
            {
                new
                {
                    type = "propose_patch",
                    files = new object[] { new { path = "new.txt", operation = "create", newContent = "new" } },
                    build = new { policy = "bogus" },
                },
            },
        });

        var result = dispatcher.ProcessRequestsDetailed([body]);

        var outcome = Assert.Single(result.PatchOutcomes);
        Assert.Equal("needs_revision", outcome.PatchStatus);
        var error = Assert.Single(outcome.BuildErrors);
        Assert.Equal("invalid_patch_policy", error.Code);
        Assert.Null(error.Path);
        Assert.Contains("build stage failed", error.Message);
        Assert.Empty(outcome.TestFailures);
    }

    [Fact]
    public void ProcessRequestsDetailed_surfaces_single_outcome_across_multiple_inputs()
    {
        using var temp = CreateRepo(("file.txt", "x"));
        var dispatcher = RealPatchDispatcher(temp.Path);

        var treeBody = JsonSerializer.Serialize(new
        {
            version = "1.0",
            id = "d1c2d3e4-0000-49d0-bf21-5d6e8a0c1f47",
            commands = new object[] { new { type = "tree", path = ".", depth = 1 } },
        });
        var proposeBody = ProposeBody("e1c2d3e4-0000-49d0-bf21-5d6e8a0c1f47", "added.txt", "content");

        var result = dispatcher.ProcessRequestsDetailed([treeBody, proposeBody]);

        var outcome = Assert.Single(result.PatchOutcomes);
        Assert.Equal(CommandTypes.ProposePatch, outcome.CommandType);
        Assert.Equal("accepted", outcome.PatchStatus);
    }

    private static string ProposeBody(string id, string path, string content) =>
        JsonSerializer.Serialize(new
        {
            version = "1.0",
            id,
            commands = new object[]
            {
                new
                {
                    type = "propose_patch",
                    files = new object[] { new { path, operation = "create", newContent = content } },
                    build = new { policy = "none" },
                    tests = new { policy = "none" },
                },
            },
        });

    private static CommandDispatcher RealPatchDispatcher(string rootPath)
    {
        var patchService = new PatchTransactionService(rootPath, "TestRoot");
        return CommandDispatcher.ForServices(
            new FileSystemContextService(rootPath),
            roslyn: null,
            session: null,
            gitStatus: new LibGit2SharpGitStatusService(rootPath),
            patchTransactions: patchService);
    }

    private static TempDirectory CreateRepo(params (string Path, string Content)[] files)
    {
        var temp = new TempDirectory();
        Repository.Init(temp.Path);
        foreach (var file in files)
            temp.CreateFile(file.Path, file.Content);

        using var repo = new Repository(temp.Path);
        LibGit2Sharp.Commands.Stage(repo, "*");
        repo.Commit("initial", Signature(), Signature());
        return temp;
    }

    private static Signature Signature() =>
        new("Test", "test@example.com", DateTimeOffset.UtcNow);

    private static string HashText(string text) =>
        ContentHash.ForBytes(Encoding.UTF8.GetBytes(text));

    private sealed class FakeRoslynNavigationService : IRoslynNavigationService
    {
        public string GetWorkspaceVersion() => "sha256:test";

        public void InvalidateWorkspace()
        {
        }

        public FindSymbolsResult FindSymbols(FindSymbolQuery query) => new()
        {
            WorkspaceVersion = GetWorkspaceVersion(),
            Matches =
            [
                new SymbolSummary
                {
                    Name = query.Name,
                    Kind = "class",
                    SymbolId = "T:Demo.Parser",
                    ProjectName = "Demo",
                    Path = "src/Parser.cs",
                    Line = 3,
                    Signature = "Demo.Parser",
                    Namespace = "Demo",
                    ContainingType = "Outer",
                    Accessibility = "public",
                },
            ],
        };

        public FindReferencesResult FindReferences(FindReferencesQuery query) => new()
        {
            WorkspaceVersion = GetWorkspaceVersion(),
            Symbol = new SymbolSummary
            {
                Name = "Parser",
                Kind = "class",
                SymbolId = query.SymbolId,
                ProjectName = "Demo",
                Path = "src/Parser.cs",
                Line = 3,
                ContainingType = "Outer",
                Accessibility = "public",
            },
            References =
            [
                new ReferenceLocation
                {
                    SymbolId = query.SymbolId ?? "M:Demo.Parser.Parse(System.String)",
                    ProjectName = "Demo.Tests",
                    Path = "tests/ParserTests.cs",
                    Line = 10,
                    Column = 17,
                    LineText = "var parser = new Parser();",
                    Kind = "call",
                },
            ],
        };

        public GotoDefinitionResult GotoDefinition(GotoDefinitionQuery query) => new()
        {
            WorkspaceVersion = GetWorkspaceVersion(),
            Definitions =
            [
                new SymbolSummary
                {
                    Name = "Parser",
                    Kind = "class",
                    SymbolId = "T:Demo.Parser",
                    ProjectName = "Demo",
                    Path = "src/Parser.cs",
                    Line = 3,
                    ContainingType = "Outer",
                    Accessibility = "public",
                },
            ],
        };

        public FindImplementationsResult FindImplementations(FindImplementationsQuery query) => new()
        {
            WorkspaceVersion = GetWorkspaceVersion(),
            Symbol = new SymbolSummary
            {
                Name = "IParser",
                Kind = "interface",
                SymbolId = query.SymbolId,
                ProjectName = "Demo",
                Path = "src/IParser.cs",
                Line = 3,
                Accessibility = "public",
            },
            Implementations =
            [
                new SymbolSummary
                {
                    Name = "Parser",
                    Kind = "class",
                    SymbolId = "T:Demo.Parser",
                    ProjectName = "Demo",
                    Path = "src/Parser.cs",
                    Line = 3,
                    Accessibility = "public",
                },
            ],
        };

        public FindCallersResult FindCallers(FindCallersQuery query) => new()
        {
            WorkspaceVersion = GetWorkspaceVersion(),
            Symbol = new SymbolSummary
            {
                Name = "Parse",
                Kind = "method",
                SymbolId = query.SymbolId ?? "M:Demo.ParserBase.Parse(System.String)",
                ProjectName = "Demo",
                Path = "src/Parser.cs",
                Line = 5,
                Accessibility = "public",
            },
            Callers =
            [
                new ReferenceLocation
                {
                    SymbolId = query.SymbolId ?? "M:Demo.Parser.Parse(System.String)",
                    ProjectName = "Demo.Tests",
                    Path = "tests/ParserTests.cs",
                    Line = 10,
                    Column = 17,
                    LineText = "Parser.Parse(text);",
                    Kind = "call",
                },
            ],
        };

        public FindDerivedTypesResult FindDerivedTypes(FindDerivedTypesQuery query) => new()
        {
            WorkspaceVersion = GetWorkspaceVersion(),
            Symbol = new SymbolSummary
            {
                Name = "ParserBase",
                Kind = "class",
                SymbolId = query.SymbolId,
                ProjectName = "Demo",
                Path = "src/ParserBase.cs",
                Line = 3,
                Accessibility = "public",
            },
            DerivedTypes =
            [
                new SymbolSummary
                {
                    Name = "Parser",
                    Kind = "class",
                    SymbolId = "T:Demo.Parser",
                    ProjectName = "Demo",
                    Path = "src/Parser.cs",
                    Line = 3,
                    Accessibility = "public",
                },
            ],
        };

        public FindOverridesResult FindOverrides(FindOverridesQuery query) => new()
        {
            WorkspaceVersion = GetWorkspaceVersion(),
            Symbol = new SymbolSummary
            {
                Name = "Parse",
                Kind = "method",
                SymbolId = query.SymbolId ?? "M:Demo.ParserBase.Parse(System.String)",
                ProjectName = "Demo",
                Path = "src/ParserBase.cs",
                Line = 5,
                ContainingType = "ParserBase",
                Accessibility = "public",
            },
            Overrides =
            [
                new SymbolSummary
                {
                    Name = "Parse",
                    Kind = "method",
                    SymbolId = "M:Demo.Parser.Parse(System.String)",
                    ProjectName = "Demo",
                    Path = "src/Parser.cs",
                    Line = 7,
                    ContainingType = "Parser",
                    Accessibility = "public",
                },
            ],
        };

        public SymbolInfoResult GetSymbolInfo(GetSymbolInfoQuery query) => new()
        {
            WorkspaceVersion = GetWorkspaceVersion(),
            Symbol = new SymbolSummary
            {
                Name = "Parser",
                Kind = "method",
                SymbolId = query.SymbolId,
                ProjectName = "Demo",
                Path = "src/Parser.cs",
                Line = 3,
                Accessibility = "public",
            },
            ImplementedInterfaces = ["System.IDisposable"],
            ReturnType = "void",
            Parameters = [new SymbolParameterInfo { Name = "text", Type = "string" }],
            IsStatic = true,
            IsAsync = false,
        };

        public GetSymbolSourceResult GetSymbolSource(GetSymbolSourceQuery query) => new()
        {
            WorkspaceVersion = GetWorkspaceVersion(),
            Symbol = new SymbolSummary
            {
                Name = "Parser",
                Kind = "method",
                SymbolId = query.SymbolId ?? "M:Demo.Parser.Parse(System.String)",
                ProjectName = "Demo",
                Path = "src/Parser.cs",
                Line = 3,
                Accessibility = "public",
            },
            Source = new SymbolSourceBlock
            {
                Path = "src/Parser.cs",
                StartLine = 3,
                EndLine = 5,
                EndColumn = 2,
                Text = "public static void Parse(string text) { }",
            },
        };

        public DocumentSymbolsResult GetDocumentSymbols(DocumentSymbolsQuery query) => new()
        {
            Path = query.RelativePath,
            Symbols =
            [
                new DocumentSymbol
                {
                    Name = "Parser",
                    Kind = "class",
                    Line = 3,
                    EndLine = 8,
                    Signature = "public static class Parser",
                    Children =
                    [
                        new DocumentSymbol
                        {
                            Name = "Parse",
                            Kind = "method",
                            Line = 5,
                            EndLine = 7,
                            Signature = "public static void Parse(string text)",
                        },
                    ],
                },
            ],
        };
    }

    private sealed class ThrowingRoslynNavigationService : IRoslynNavigationService
    {
        private readonly Exception _exception;

        public ThrowingRoslynNavigationService(Exception exception)
        {
            _exception = exception;
        }

        public DocumentSymbolsResult GetDocumentSymbols(DocumentSymbolsQuery query) =>
            throw _exception;

        public string GetWorkspaceVersion() =>
            throw _exception;

        public void InvalidateWorkspace() =>
            throw _exception;

        public FindSymbolsResult FindSymbols(FindSymbolQuery query) =>
            throw _exception;

        public FindReferencesResult FindReferences(FindReferencesQuery query) =>
            throw _exception;

        public GotoDefinitionResult GotoDefinition(GotoDefinitionQuery query) =>
            throw _exception;

        public FindImplementationsResult FindImplementations(FindImplementationsQuery query) =>
            throw _exception;

        public FindCallersResult FindCallers(FindCallersQuery query) =>
            throw _exception;

        public FindDerivedTypesResult FindDerivedTypes(FindDerivedTypesQuery query) =>
            throw _exception;

        public FindOverridesResult FindOverrides(FindOverridesQuery query) =>
            throw _exception;

        public SymbolInfoResult GetSymbolInfo(GetSymbolInfoQuery query) =>
            throw _exception;

        public GetSymbolSourceResult GetSymbolSource(GetSymbolSourceQuery query) =>
            throw _exception;
    }

    private sealed class CapturingRoslynNavigationService : IRoslynNavigationService
    {
        public FindReferencesQuery? FindReferencesQuery { get; private set; }

        public string GetWorkspaceVersion() => "sha256:test";

        public void InvalidateWorkspace()
        {
        }

        public DocumentSymbolsResult GetDocumentSymbols(DocumentSymbolsQuery query) => new()
        {
            Path = query.RelativePath,
            Symbols = [],
        };

        public FindSymbolsResult FindSymbols(FindSymbolQuery query) => new()
        {
            WorkspaceVersion = GetWorkspaceVersion(),
            Matches = [],
        };

        public FindReferencesResult FindReferences(FindReferencesQuery query)
        {
            FindReferencesQuery = query;
            return new FindReferencesResult { WorkspaceVersion = GetWorkspaceVersion(), References = [] };
        }

        public GotoDefinitionResult GotoDefinition(GotoDefinitionQuery query) => new()
        {
            WorkspaceVersion = GetWorkspaceVersion(),
            Definitions = [],
        };

        public FindImplementationsResult FindImplementations(FindImplementationsQuery query) => new()
        {
            WorkspaceVersion = GetWorkspaceVersion(),
            Implementations = [],
        };

        public FindCallersResult FindCallers(FindCallersQuery query) => new()
        {
            WorkspaceVersion = GetWorkspaceVersion(),
            Callers = [],
        };

        public FindDerivedTypesResult FindDerivedTypes(FindDerivedTypesQuery query) => new()
        {
            WorkspaceVersion = GetWorkspaceVersion(),
            DerivedTypes = [],
        };

        public FindOverridesResult FindOverrides(FindOverridesQuery query) => new()
        {
            WorkspaceVersion = GetWorkspaceVersion(),
            Overrides = [],
        };

        public SymbolInfoResult GetSymbolInfo(GetSymbolInfoQuery query) => new()
        {
            WorkspaceVersion = GetWorkspaceVersion(),
        };

        public GetSymbolSourceResult GetSymbolSource(GetSymbolSourceQuery query) => new()
        {
            WorkspaceVersion = GetWorkspaceVersion(),
        };
    }

    private sealed class FakeGitStatusService : IGitStatusService
    {
        public GitStatusInfo GetStatus() => new()
        {
            IsRepository = true,
            IsClean = false,
            Branch = "main",
            HeadSha = "abc123",
            ChangedFiles =
            [
                new GitStatusFile
                {
                    Path = "src/file.cs",
                    Status = "modified_unstaged",
                },
            ],
        };
    }

    private sealed class FakeContextSession : IContextSession
    {
        private readonly string _rootPath;

        public FakeContextSession(string rootPath)
        {
            _rootPath = rootPath;
        }

        public CurrentContextInfo GetCurrentContext() => new()
        {
            RootProfile = new RootProfileInfo
            {
                Name = "TestRoot",
                Path = _rootPath,
                IsCurrent = true,
            },
            Target = new TargetProfileInfo
            {
                Name = "TestTarget",
                Process = "test",
                IsCurrent = true,
            },
            Server = new ServerInfo
            {
                Version = "test",
            },
            Protocol = new ProtocolInfo(),
        };

        public IReadOnlyList<RootProfileInfo> ListRoots() => [GetCurrentContext().RootProfile];

        public IReadOnlyList<TargetProfileInfo> ListTargets() => [GetCurrentContext().Target];

        public CurrentContextInfo SetRoot(string name) => GetCurrentContext();

        public void ApplyPendingRootSwitch()
        {
        }
    }

    private sealed class FakePatchTransactionService : IPatchTransactionService
    {
        public ProposePatchRequest? ProposedRequest { get; private set; }

        public AmendPatchRequest? AmendedRequest { get; private set; }

        public ValidatePatchRequest? ValidateRequest { get; private set; }

        public bool HasActivePatch { get; private set; }

        public bool DeferAcceptanceByDefault { get; set; }

        public PatchTransactionResult Accept(string patchId) => Result("accepted", applied: true);

        public PatchTransactionResult Propose(ProposePatchRequest request)
        {
            ProposedRequest = request;
            return Result("accepted", applied: true);
        }

        public PatchTransactionResult Amend(AmendPatchRequest request)
        {
            AmendedRequest = request;
            return Result("accepted", applied: true) with { Revision = 2 };
        }

        public PatchValidationResult Validate(ValidatePatchRequest request)
        {
            ValidateRequest = request;
            return new PatchValidationResult
            {
                Valid = true,
                Mode = string.IsNullOrWhiteSpace(request.PatchId) ? "propose" : "amend",
                PatchId = request.PatchId,
                BaseRevision = request.BaseRevision,
                Applied = false,
                DiffVerified = false,
                Build = new PatchStageResult { Status = "validated", Policy = request.Build?.Policy ?? "none" },
                Tests = new PatchStageResult { Status = "skipped", Policy = request.Tests?.Policy ?? "none" },
                Files =
                [
                    new PatchFileState
                    {
                        Path = "src/file.txt",
                        Operation = "replace",
                        OldContentHash = "sha256:" + new string('0', 64),
                        CurrentContentHash = "sha256:" + new string('1', 64),
                        LastRevision = 0,
                    },
                ],
            };
        }

        public PatchTransactionResult Current() =>
            HasActivePatch ? Result("accepted", applied: true) : new PatchTransactionResult { PatchStatus = "none" };

        public PatchTransactionResult Revert(string patchId)
        {
            throw new PatchValidationException(ProtocolErrorCodes.PatchNotActive, "No active patch exists.");
        }

        private static PatchTransactionResult Result(string status, bool applied) => new()
        {
            PatchStatus = status,
            PatchId = "p-test",
            Revision = 1,
            Applied = applied,
            DiffVerified = applied,
            Build = new PatchStageResult { Status = "skipped", Policy = "none" },
            Tests = new PatchStageResult { Status = "skipped", Policy = "none" },
            Files =
            [
                new PatchFileState
                {
                    Path = "src/file.txt",
                    Operation = "replace",
                    OldContentHash = "sha256:" + new string('0', 64),
                    CurrentContentHash = "sha256:" + new string('1', 64),
                    LastRevision = 1,
                },
            ],
        };
    }

    private sealed class PolicyFailurePatchTransactionService : IPatchTransactionService
    {
        public bool HasActivePatch => false;

        public bool DeferAcceptanceByDefault { get; set; }

        public PatchTransactionResult Accept(string patchId) => Result();

        public PatchTransactionResult Propose(ProposePatchRequest request) => Result();

        public PatchTransactionResult Amend(AmendPatchRequest request) => Result();

        public PatchValidationResult Validate(ValidatePatchRequest request) => new()
        {
            Valid = true,
            Mode = "propose",
            Applied = false,
            DiffVerified = false,
        };

        public PatchTransactionResult Current() => Result();

        public PatchTransactionResult Revert(string patchId) => Result() with { PatchStatus = "reverted" };

        private static PatchTransactionResult Result() => new()
        {
            PatchStatus = "needs_revision",
            PatchId = "p-test",
            Revision = 1,
            Applied = true,
            DiffVerified = true,
            LastFailureStage = "build",
            Build = new PatchStageResult
            {
                Status = "failed",
                Policy = "bogus",
                Diagnostics =
                [
                    new BuildDiagnostic
                    {
                        Kind = "error",
                        Code = "invalid_patch_policy",
                        Message = "Unsupported build policy 'bogus'.",
                    },
                ],
            },
            Tests = new PatchStageResult { Status = "skipped", Policy = "none" },
            Files =
            [
                new PatchFileState
                {
                    Path = "new.txt",
                    Operation = "create",
                    CurrentContentHash = "sha256:" + new string('1', 64),
                    LastRevision = 1,
                },
            ],
        };
    }

    private sealed class WarningPatchTransactionService : IPatchTransactionService
    {
        public bool HasActivePatch => false;

        public bool DeferAcceptanceByDefault { get; set; }

        public PatchTransactionResult Accept(string patchId) => Result();

        public PatchTransactionResult Propose(ProposePatchRequest request) => Result();

        public PatchTransactionResult Amend(AmendPatchRequest request) => Result();

        public PatchValidationResult Validate(ValidatePatchRequest request) => new()
        {
            Valid = true,
            Mode = "propose",
            Applied = false,
            DiffVerified = false,
            Build = new PatchStageResult { Status = "skipped", Policy = "none" },
            Tests = new PatchStageResult { Status = "skipped", Policy = "none" },
            Warnings =
            [
                new PatchWarning
                {
                    Code = "json_formatting_changed",
                    Message = "JSON formatting changed.",
                    Path = "appsettings.json",
                    EditIndex = 0,
                    Kind = "json_set",
                },
            ],
        };

        public PatchTransactionResult Current() => Result();

        public PatchTransactionResult Revert(string patchId) => Result() with { PatchStatus = "reverted" };

        private static PatchTransactionResult Result() => new()
        {
            PatchStatus = "accepted",
            PatchId = "p-test",
            Revision = 1,
            Applied = true,
            DiffVerified = true,
            Warnings =
            [
                new PatchWarning
                {
                    Code = "json_formatting_changed",
                    Message = "JSON formatting changed.",
                    Path = "appsettings.json",
                    EditIndex = 0,
                    Kind = "json_set",
                },
            ],
        };
    }

    private sealed class ThrowingPatchTransactionService : IPatchTransactionService
    {
        private readonly Exception _exception;

        public ThrowingPatchTransactionService(Exception exception)
        {
            _exception = exception;
        }

        public bool HasActivePatch => false;

        public bool DeferAcceptanceByDefault { get; set; }

        public PatchTransactionResult Accept(string patchId) => throw _exception;

        public PatchTransactionResult Propose(ProposePatchRequest request) => throw _exception;

        public PatchTransactionResult Amend(AmendPatchRequest request) => throw _exception;

        public PatchValidationResult Validate(ValidatePatchRequest request) => throw _exception;

        public PatchTransactionResult Current() => throw _exception;

        public PatchTransactionResult Revert(string patchId) => throw _exception;
    }
}
