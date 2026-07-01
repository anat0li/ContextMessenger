using System.Text.Json.Nodes;
using ContextMessenger.Core.Patching;
using ContextMessenger.Core.Roslyn;
using LibGit2Sharp;
using TestResult = ContextMessenger.Core.Patching.TestResult;

namespace ContextMessenger.Patching.Tests;

public sealed class PatchTransactionServiceTests
{
    [Fact]
    public void Propose_rejects_dirty_repository()
    {
        using var temp = CreateRepo();
        temp.CreateFile("dirty.txt", "dirty");
        var service = new PatchTransactionService(temp.Path);

        var ex = Assert.Throws<PatchValidationException>(() => service.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "new" }],
        }));

        Assert.Equal("dirty_working_tree", ex.Code);
    }

    [Fact]
    public void Propose_rejects_hash_mismatch()
    {
        using var temp = CreateRepo(("file.txt", "old"));
        var service = new PatchTransactionService(temp.Path);

        var ex = Assert.Throws<PatchValidationException>(() => service.Propose(new ProposePatchRequest
        {
            Files =
            [
                new PatchFileOperation
                {
                    Path = "file.txt",
                    Operation = PatchFileOperationKind.Replace,
                    OldContentHash = "sha256:" + new string('0', 64),
                    NewContent = "new",
                },
            ],
        }));

        Assert.Equal("content_hash_mismatch", ex.Code);
        Assert.Equal("old", File.ReadAllText(Path.Combine(temp.Path, "file.txt")));
    }

    [Fact]
    public void Propose_create_over_existing_file_does_not_delete_existing_file()
    {
        using var temp = CreateRepo(("file.txt", "original"));
        var filePath = Path.Combine(temp.Path, "file.txt");
        var service = new PatchTransactionService(temp.Path);

        var ex = Assert.Throws<PatchValidationException>(() => service.Propose(new ProposePatchRequest
        {
            Files =
            [
                new PatchFileOperation
                {
                    Path = "file.txt",
                    Operation = PatchFileOperationKind.Create,
                    NewContent = "must not apply",
                },
            ],
        }));

        Assert.Equal("file_exists", ex.Code);
        Assert.Equal("original", File.ReadAllText(filePath));
        Assert.Equal("none", service.Current().PatchStatus);
        Assert.True(new LibGit2SharpGitStatusService(temp.Path).GetStatus().IsClean);
    }

    [Fact]
    public void Propose_rejects_empty_files_and_edits_as_invalid_parameters()
    {
        using var temp = CreateRepo();
        var service = new PatchTransactionService(temp.Path);

        var ex = Assert.Throws<PatchValidationException>(() => service.Propose(new ProposePatchRequest()));

        Assert.Equal("invalid_parameters", ex.Code);
    }

    [Fact]
    public void Propose_applies_stages_and_closes_accepted_patch()
    {
        using var temp = CreateRepo(("replace.txt", "old"), ("delete.txt", "delete"));
        var replacePath = Path.Combine(temp.Path, "replace.txt");
        var deletePath = Path.Combine(temp.Path, "delete.txt");
        var service = new PatchTransactionService(temp.Path);

        var result = service.Propose(new ProposePatchRequest
        {
            Title = "Patch",
            CommitMessage = "Patch message",
            Files =
            [
                new PatchFileOperation
                {
                    Path = "replace.txt",
                    Operation = PatchFileOperationKind.Replace,
                    OldContentHash = ContentHash.ForFile(replacePath),
                    NewContent = "new",
                },
                new PatchFileOperation
                {
                    Path = "create.txt",
                    Operation = PatchFileOperationKind.Create,
                    NewContent = "created",
                },
                new PatchFileOperation
                {
                    Path = "delete.txt",
                    Operation = PatchFileOperationKind.Delete,
                    OldContentHash = ContentHash.ForFile(deletePath),
                },
            ],
        });

        Assert.Equal("accepted", result.PatchStatus);
        Assert.True(result.Applied);
        Assert.True(result.DiffVerified);
        Assert.NotNull(result.PatchId);
        Assert.Equal("skipped", result.Build!.Status);
        Assert.Equal("none", result.Build.Policy);
        Assert.Equal("new", File.ReadAllText(replacePath));
        Assert.Equal("created", File.ReadAllText(Path.Combine(temp.Path, "create.txt")));
        Assert.False(File.Exists(deletePath));

        Assert.Equal("none", service.Current().PatchStatus);
        var status = new LibGit2SharpGitStatusService(temp.Path).GetStatus();
        Assert.False(status.IsClean);
        Assert.Contains(status.ChangedFiles, f => f is { Path: "replace.txt", Status: "staged_modified" });
        Assert.Contains(status.ChangedFiles, f => f is { Path: "create.txt", Status: "staged_new" });
        Assert.Contains(status.ChangedFiles, f => f is { Path: "delete.txt", Status: "staged_deleted" });
    }

    [Fact]
    public void Propose_with_replace_exact_edit_applies_stages_and_closes_accepted_patch()
    {
        using var temp = CreateRepo(("file.txt", "one two three"));
        var filePath = Path.Combine(temp.Path, "file.txt");
        var service = new PatchTransactionService(temp.Path);

        var result = service.Propose(new ProposePatchRequest
        {
            Edits =
            [
                new PatchEditOperation
                {
                    Path = "file.txt",
                    Kind = "replace_exact",
                    OldText = "two",
                    NewText = "TWO",
                    ExpectedFileHash = ContentHash.ForFile(filePath),
                },
            ],
        });

        Assert.Equal("accepted", result.PatchStatus);
        Assert.Equal("one TWO three", File.ReadAllText(filePath));
        Assert.Equal("none", service.Current().PatchStatus);
        Assert.Contains(new LibGit2SharpGitStatusService(temp.Path).GetStatus().ChangedFiles,
            f => f is { Path: "file.txt", Status: "staged_modified" });
    }

    [Fact]
    public void Propose_edit_expected_anchor_hash_allows_matching_anchor()
    {
        using var temp = CreateRepo(("file.txt", "one\ntwo\nthree\n"));
        var filePath = Path.Combine(temp.Path, "file.txt");
        var service = new PatchTransactionService(temp.Path);

        var result = service.Propose(new ProposePatchRequest
        {
            Edits =
            [
                new PatchEditOperation
                {
                    Path = "file.txt",
                    Kind = "replace_exact",
                    OldText = "two\n",
                    NewText = "TWO\n",
                    ExpectedAnchorHash = HashText("two\n"),
                },
            ],
        });

        Assert.Equal("accepted", result.PatchStatus);
        Assert.Equal("one\nTWO\nthree\n", File.ReadAllText(filePath));
        Assert.Equal("none", service.Current().PatchStatus);
    }

    [Fact]
    public void Propose_edit_expected_anchor_hash_mismatch_does_not_change_working_tree()
    {
        using var temp = CreateRepo(("file.txt", "one\ntwo\nthree\n"));
        var filePath = Path.Combine(temp.Path, "file.txt");
        var service = new PatchTransactionService(temp.Path);

        var ex = Assert.Throws<PatchValidationException>(() => service.Propose(new ProposePatchRequest
        {
            Edits =
            [
                new PatchEditOperation
                {
                    Path = "file.txt",
                    Kind = "replace_exact",
                    OldText = "two\n",
                    NewText = "TWO\n",
                    ExpectedAnchorHash = HashText("wrong\n"),
                },
            ],
        }));

        Assert.Equal("edit_conflict", ex.Code);
        Assert.Equal("file.txt", ex.Path);
        Assert.Equal(0, ex.EditIndex);
        Assert.Equal("replace_exact", ex.Kind);
        Assert.Equal("expectedAnchorHash", ex.HashField);
        Assert.Equal(HashText("wrong\n"), ex.ExpectedHash);
        Assert.Equal(HashText("two\n"), ex.ActualHash);
        Assert.Equal("oldText", ex.HashTarget);
        Assert.Equal("one\ntwo\nthree\n", File.ReadAllText(filePath));
        Assert.True(new LibGit2SharpGitStatusService(temp.Path).GetStatus().IsClean);
    }

    [Fact]
    public void Propose_edit_malformed_expected_anchor_hash_returns_hash_details()
    {
        using var temp = CreateRepo(("file.txt", "one\ntwo\nthree\n"));
        var service = new PatchTransactionService(temp.Path);

        var ex = Assert.Throws<PatchValidationException>(() => service.Propose(new ProposePatchRequest
        {
            Edits =
            [
                new PatchEditOperation
                {
                    Path = "file.txt",
                    Kind = "replace_exact",
                    OldText = "two\n",
                    NewText = "TWO\n",
                    ExpectedAnchorHash = "sha256:XYZ",
                },
            ],
        }));

        Assert.Equal("invalid_content_hash", ex.Code);
        Assert.Equal("file.txt", ex.Path);
        Assert.Equal(0, ex.EditIndex);
        Assert.Equal("replace_exact", ex.Kind);
        Assert.Equal("expectedAnchorHash", ex.HashField);
        Assert.Equal("oldText", ex.HashTarget);
        Assert.Equal("sha256:<64 lowercase hex characters>", ex.ExpectedFormat);
    }

    [Fact]
    public void Propose_second_edit_can_guard_anchor_created_by_first_edit()
    {
        using var temp = CreateRepo(("file.txt", "one\nthree\n"));
        var filePath = Path.Combine(temp.Path, "file.txt");
        var service = new PatchTransactionService(temp.Path);

        var result = service.Propose(new ProposePatchRequest
        {
            Edits =
            [
                new PatchEditOperation
                {
                    Path = "file.txt",
                    Kind = "insert_after_exact",
                    Anchor = "one\n",
                    Text = "two\n",
                },
                new PatchEditOperation
                {
                    Path = "file.txt",
                    Kind = "replace_exact",
                    OldText = "two\n",
                    NewText = "TWO\n",
                    ExpectedAnchorHash = HashText("two\n"),
                },
            ],
        });

        Assert.Equal("accepted", result.PatchStatus);
        Assert.Equal("one\nTWO\nthree\n", File.ReadAllText(filePath));
    }

    [Fact]
    public void Propose_with_insert_and_delete_exact_edits_applies_in_order()
    {
        using var temp = CreateRepo(("file.txt", "alpha\nomega\n"));
        var filePath = Path.Combine(temp.Path, "file.txt");
        var service = new PatchTransactionService(temp.Path);

        service.Propose(new ProposePatchRequest
        {
            Edits =
            [
                new PatchEditOperation
                {
                    Path = "file.txt",
                    Kind = "insert_after_exact",
                    Anchor = "alpha\n",
                    Text = "beta\n",
                    ExpectedFileHash = ContentHash.ForFile(filePath),
                },
                new PatchEditOperation
                {
                    Path = "file.txt",
                    Kind = "insert_before_exact",
                    Anchor = "omega\n",
                    Text = "gamma\n",
                },
                new PatchEditOperation
                {
                    Path = "file.txt",
                    Kind = "delete_exact",
                    OldText = "beta\n",
                },
            ],
        });

        Assert.Equal("alpha\ngamma\nomega\n", File.ReadAllText(filePath));
    }

    [Fact]
    public void Propose_exact_anchor_matches_crlf_file_with_lf_anchor()
    {
        using var temp = CreateRepo(("file.txt", "alpha\r\nbeta\r\nomega\r\n"));
        var service = new PatchTransactionService(temp.Path);

        service.Propose(new ProposePatchRequest
        {
            Edits =
            [
                new PatchEditOperation
                {
                    Path = "file.txt",
                    Kind = "replace_exact",
                    OldText = "beta\n",
                    NewText = "BETA\n",
                },
            ],
        });

        Assert.Equal("alpha\r\nBETA\r\nomega\r\n", File.ReadAllText(Path.Combine(temp.Path, "file.txt")));
    }

    [Fact]
    public void Propose_insert_after_exact_uses_actual_crlf_anchor_length()
    {
        using var temp = CreateRepo(("file.txt", "alpha\r\nbeta\r\n"));
        var service = new PatchTransactionService(temp.Path);

        service.Propose(new ProposePatchRequest
        {
            Edits =
            [
                new PatchEditOperation
                {
                    Path = "file.txt",
                    Kind = "insert_after_exact",
                    Anchor = "alpha\n",
                    Text = "inserted\n",
                },
            ],
        });

        Assert.Equal("alpha\r\ninserted\r\nbeta\r\n", File.ReadAllText(Path.Combine(temp.Path, "file.txt")));
    }

    [Theory]
    [MemberData(nameof(ReplaceLinesCases))]
    public void Propose_with_replace_lines_replaces_expected_range(
        string original,
        int startLine,
        int endLine,
        string oldSlice,
        string newText,
        string expected)
    {
        using var temp = CreateRepo(("file.txt", original));
        var filePath = Path.Combine(temp.Path, "file.txt");
        var service = new PatchTransactionService(temp.Path);

        service.Propose(new ProposePatchRequest
        {
            Edits =
            [
                new PatchEditOperation
                {
                    Path = "file.txt",
                    Kind = "replace_lines",
                    StartLine = startLine,
                    EndLine = endLine,
                    OldRangeHash = HashText(oldSlice),
                    NewText = newText,
                    ExpectedFileHash = ContentHash.ForFile(filePath),
                },
            ],
        });

        Assert.Equal(expected, File.ReadAllText(filePath));
    }

    [Fact]
    public void Propose_replace_lines_hash_mismatch_does_not_change_working_tree()
    {
        using var temp = CreateRepo(("file.txt", "one\ntwo\nthree\n"));
        var filePath = Path.Combine(temp.Path, "file.txt");
        var service = new PatchTransactionService(temp.Path);

        var ex = Assert.Throws<PatchValidationException>(() => service.Propose(new ProposePatchRequest
        {
            Edits =
            [
                new PatchEditOperation
                {
                    Path = "file.txt",
                    Kind = "replace_lines",
                    StartLine = 2,
                    EndLine = 2,
                    OldRangeHash = HashText("wrong\n"),
                    NewText = "TWO\n",
                },
            ],
        }));

        Assert.Equal("edit_range_hash_mismatch", ex.Code);
        Assert.Equal("file.txt", ex.Path);
        Assert.Equal(0, ex.EditIndex);
        Assert.Equal("replace_lines", ex.Kind);
        Assert.Equal("oldRangeHash", ex.HashField);
        Assert.Equal(HashText("wrong\n"), ex.ExpectedHash);
        Assert.Equal(HashText("two\n"), ex.ActualHash);
        Assert.Equal("lineRange", ex.HashTarget);
        Assert.Equal("one\ntwo\nthree\n", File.ReadAllText(filePath));
        Assert.True(new LibGit2SharpGitStatusService(temp.Path).GetStatus().IsClean);
    }

    [Fact]
    public void Propose_replace_lines_requires_old_range_hash()
    {
        using var temp = CreateRepo(("file.txt", "one\ntwo\n"));
        var service = new PatchTransactionService(temp.Path);

        var ex = Assert.Throws<PatchValidationException>(() => service.Propose(new ProposePatchRequest
        {
            Edits =
            [
                new PatchEditOperation
                {
                    Path = "file.txt",
                    Kind = "replace_lines",
                    StartLine = 1,
                    EndLine = 1,
                    NewText = "ONE\n",
                },
            ],
        }));

        Assert.Equal("invalid_parameters", ex.Code);
        Assert.Equal("file.txt", ex.Path);
        Assert.Equal(0, ex.EditIndex);
        Assert.Equal("replace_lines", ex.Kind);
    }

    [Fact]
    public void Propose_replace_lines_rejects_invalid_range()
    {
        using var temp = CreateRepo(("file.txt", "one\ntwo\n"));
        var service = new PatchTransactionService(temp.Path);

        var ex = Assert.Throws<PatchValidationException>(() => service.Propose(new ProposePatchRequest
        {
            Edits =
            [
                new PatchEditOperation
                {
                    Path = "file.txt",
                    Kind = "replace_lines",
                    StartLine = 2,
                    EndLine = 1,
                    OldRangeHash = HashText("two\n"),
                    NewText = "TWO\n",
                },
            ],
        }));

        Assert.Equal("invalid_parameters", ex.Code);
        Assert.Equal("file.txt", ex.Path);
        Assert.Equal(0, ex.EditIndex);
        Assert.Equal("replace_lines", ex.Kind);
    }

    [Fact]
    public void Propose_json_set_replaces_existing_property_and_warns()
    {
        using var temp = CreateRepo(("appsettings.json", "{\"Feature\":{\"Enabled\":false,\"Name\":\"old\"}}"));
        var filePath = Path.Combine(temp.Path, "appsettings.json");
        var service = new PatchTransactionService(temp.Path);

        var result = service.Propose(new ProposePatchRequest
        {
            Edits =
            [
                new PatchEditOperation
                {
                    Path = "appsettings.json",
                    Kind = "json_set",
                    Pointer = "/Feature/Enabled",
                    ValueSpecified = true,
                    Value = JsonNode.Parse("true"),
                },
            ],
        });

        var root = JsonNode.Parse(File.ReadAllText(filePath))!;
        Assert.True(root["Feature"]!["Enabled"]!.GetValue<bool>());
        Assert.Equal("old", root["Feature"]!["Name"]!.GetValue<string>());
        var warning = Assert.Single(result.Warnings);
        Assert.Equal("json_formatting_changed", warning.Code);
        Assert.Equal("appsettings.json", warning.Path);
        Assert.Equal(0, warning.EditIndex);
        Assert.Equal("json_set", warning.Kind);
    }

    [Fact]
    public void Propose_json_set_replaces_array_item_and_escaped_property()
    {
        using var temp = CreateRepo(("appsettings.json", "{\"Items\":[\"a\",\"b\"],\"a/b\":\"old\"}"));
        var filePath = Path.Combine(temp.Path, "appsettings.json");
        var service = new PatchTransactionService(temp.Path);

        service.Propose(new ProposePatchRequest
        {
            Edits =
            [
                new PatchEditOperation
                {
                    Path = "appsettings.json",
                    Kind = "json_set",
                    Pointer = "/Items/1",
                    ValueSpecified = true,
                    Value = JsonNode.Parse("\"B\""),
                },
                new PatchEditOperation
                {
                    Path = "appsettings.json",
                    Kind = "json_set",
                    Pointer = "/a~1b",
                    ValueSpecified = true,
                    Value = JsonNode.Parse("\"new\""),
                },
            ],
        });

        var root = JsonNode.Parse(File.ReadAllText(filePath))!;
        Assert.Equal("B", root["Items"]![1]!.GetValue<string>());
        Assert.Equal("new", root["a/b"]!.GetValue<string>());
    }

    [Fact]
    public void Propose_json_set_unresolved_pointer_does_not_change_working_tree()
    {
        using var temp = CreateRepo(("appsettings.json", "{\"Feature\":{\"Enabled\":false}}"));
        var filePath = Path.Combine(temp.Path, "appsettings.json");
        var service = new PatchTransactionService(temp.Path);

        var ex = Assert.Throws<PatchValidationException>(() => service.Propose(new ProposePatchRequest
        {
            Edits =
            [
                new PatchEditOperation
                {
                    Path = "appsettings.json",
                    Kind = "json_set",
                    Pointer = "/Feature/Missing",
                    ValueSpecified = true,
                    Value = JsonNode.Parse("true"),
                },
            ],
        }));

        Assert.Equal("edit_anchor_not_found", ex.Code);
        Assert.Equal("appsettings.json", ex.Path);
        Assert.Equal(0, ex.EditIndex);
        Assert.Equal("json_set", ex.Kind);
        Assert.Equal(0, ex.MatchCount);
        Assert.Equal("{\"Feature\":{\"Enabled\":false}}", File.ReadAllText(filePath));
        Assert.True(new LibGit2SharpGitStatusService(temp.Path).GetStatus().IsClean);
    }

    [Fact]
    public void Propose_json_set_invalid_pointer_does_not_change_working_tree()
    {
        using var temp = CreateRepo(("appsettings.json", "{\"Feature\":{\"Enabled\":false}}"));
        var filePath = Path.Combine(temp.Path, "appsettings.json");
        var service = new PatchTransactionService(temp.Path);

        var ex = Assert.Throws<PatchValidationException>(() => service.Propose(new ProposePatchRequest
        {
            Edits =
            [
                new PatchEditOperation
                {
                    Path = "appsettings.json",
                    Kind = "json_set",
                    Pointer = "Feature/Enabled",
                    ValueSpecified = true,
                    Value = JsonNode.Parse("true"),
                },
            ],
        }));

        Assert.Equal("invalid_parameters", ex.Code);
        Assert.Equal("appsettings.json", ex.Path);
        Assert.Equal(0, ex.EditIndex);
        Assert.Equal("json_set", ex.Kind);
        Assert.Equal("{\"Feature\":{\"Enabled\":false}}", File.ReadAllText(filePath));
    }

    [Fact]
    public void Propose_json_set_invalid_json_does_not_change_working_tree()
    {
        using var temp = CreateRepo(("appsettings.json", "{ invalid json"));
        var filePath = Path.Combine(temp.Path, "appsettings.json");
        var service = new PatchTransactionService(temp.Path);

        var ex = Assert.Throws<PatchValidationException>(() => service.Propose(new ProposePatchRequest
        {
            Edits =
            [
                new PatchEditOperation
                {
                    Path = "appsettings.json",
                    Kind = "json_set",
                    Pointer = "/Feature",
                    ValueSpecified = true,
                    Value = JsonNode.Parse("true"),
                },
            ],
        }));

        Assert.Equal("invalid_parameters", ex.Code);
        Assert.Equal("appsettings.json", ex.Path);
        Assert.Equal(0, ex.EditIndex);
        Assert.Equal("json_set", ex.Kind);
        Assert.Equal("{ invalid json", File.ReadAllText(filePath));
        Assert.True(new LibGit2SharpGitStatusService(temp.Path).GetStatus().IsClean);
    }

    [Fact]
    public void Propose_replace_symbol_source_replaces_resolved_source_and_invalidates_workspace()
    {
        const string oldSource = "    public string Parse() => \"old\";";
        const string newSource = "    public string Parse() => \"new\";";
        using var temp = CreateRepo(("Parser.cs", "namespace Demo;\n\npublic sealed class Parser\n{\n" + oldSource + "\n}\n"));
        var filePath = Path.Combine(temp.Path, "Parser.cs");
        var roslyn = new FakeRoslynNavigationService(new GetSymbolSourceResult
        {
            Symbol = new SymbolSummary { Name = "Parse", Kind = "method", SymbolId = "M:Demo.Parser.Parse" },
            Source = new SymbolSourceBlock
            {
                Path = "Parser.cs",
                StartLine = 5,
                StartColumn = 1,
                EndLine = 5,
                EndColumn = oldSource.Length + 1,
                Text = oldSource,
            },
        });
        var service = new PatchTransactionService(temp.Path, "TestRoot", roslynNavigation: roslyn);

        service.Propose(new ProposePatchRequest
        {
            Edits =
            [
                new PatchEditOperation
                {
                    Kind = "replace_symbol_source",
                    SymbolId = "M:Demo.Parser.Parse",
                    OldSourceHash = HashText(oldSource),
                    NewText = newSource,
                },
            ],
        });

        Assert.Contains(newSource, File.ReadAllText(filePath));
        Assert.DoesNotContain(oldSource, File.ReadAllText(filePath));
        Assert.Equal(1, roslyn.InvalidateCount);
    }

    [Fact]
    public void Propose_replace_symbol_source_preserves_csharp_escaped_literals()
    {
        const string oldSource = "    public string Parse() => \"old\";";
        const string newSource = """
            public string Parse()
            {
                const string NewLine = "\n";
                const string CrLf = "\r\n";
                const string Slash = "\\";
                const string Quote = "\"quoted\"";
                var value = 42;
                return $"NewLine={NewLine}; CrLf={CrLf}; Slash={Slash}; Quote={Quote}; Value={value}";
            }
        """;
        using var temp = CreateRepo(("Parser.cs", "namespace Demo;\n\npublic sealed class Parser\n{\n" + oldSource + "\n}\n"));
        var filePath = Path.Combine(temp.Path, "Parser.cs");
        var roslyn = new FakeRoslynNavigationService(new GetSymbolSourceResult
        {
            Symbol = new SymbolSummary { Name = "Parse", Kind = "method", SymbolId = "M:Demo.Parser.Parse" },
            Source = new SymbolSourceBlock
            {
                Path = "Parser.cs",
                StartLine = 5,
                StartColumn = 1,
                EndLine = 5,
                EndColumn = oldSource.Length + 1,
                Text = oldSource,
            },
        });
        var service = new PatchTransactionService(temp.Path, "TestRoot", roslynNavigation: roslyn);

        service.Propose(new ProposePatchRequest
        {
            Edits =
            [
                new PatchEditOperation
                {
                    Kind = "replace_symbol_source",
                    SymbolId = "M:Demo.Parser.Parse",
                    OldSourceHash = HashText(oldSource),
                    NewText = newSource,
                },
            ],
        });

        var content = File.ReadAllText(filePath);
        Assert.Contains("const string NewLine = \"\\n\";", content);
        Assert.Contains("const string CrLf = \"\\r\\n\";", content);
        Assert.Contains("const string Slash = \"\\\\\";", content);
        Assert.Contains("const string Quote = \"\\\"quoted\\\"\";", content);
        Assert.Contains("return $\"NewLine={NewLine}; CrLf={CrLf}; Slash={Slash}; Quote={Quote}; Value={value}\";", content);
    }

    [Fact]
    public void Propose_replace_symbol_source_hash_mismatch_does_not_change_working_tree()
    {
        const string oldSource = "    public string Parse() => \"old\";";
        using var temp = CreateRepo(("Parser.cs", "namespace Demo;\n\npublic sealed class Parser\n{\n" + oldSource + "\n}\n"));
        var filePath = Path.Combine(temp.Path, "Parser.cs");
        var roslyn = new FakeRoslynNavigationService(new GetSymbolSourceResult
        {
            Symbol = new SymbolSummary { Name = "Parse", Kind = "method", SymbolId = "M:Demo.Parser.Parse" },
            Source = new SymbolSourceBlock
            {
                Path = "Parser.cs",
                StartLine = 5,
                StartColumn = 1,
                EndLine = 5,
                EndColumn = oldSource.Length + 1,
                Text = oldSource,
            },
        });
        var service = new PatchTransactionService(temp.Path, "TestRoot", roslynNavigation: roslyn);

        var ex = Assert.Throws<PatchValidationException>(() => service.Propose(new ProposePatchRequest
        {
            Edits =
            [
                new PatchEditOperation
                {
                    Kind = "replace_symbol_source",
                    SymbolId = "M:Demo.Parser.Parse",
                    OldSourceHash = HashText("wrong"),
                    NewText = "    public string Parse() => \"new\";",
                },
            ],
        }));

        Assert.Equal("semantic_span_hash_mismatch", ex.Code);
        Assert.Equal("Parser.cs", ex.Path);
        Assert.Equal(0, ex.EditIndex);
        Assert.Equal("replace_symbol_source", ex.Kind);
        Assert.Equal("oldSourceHash", ex.HashField);
        Assert.Equal("source", ex.HashTarget);
        Assert.Equal(HashText("wrong"), ex.ExpectedHash);
        Assert.Equal(HashText(oldSource), ex.ActualHash);
        Assert.Contains(oldSource, File.ReadAllText(filePath));
        Assert.True(new LibGit2SharpGitStatusService(temp.Path).GetStatus().IsClean);
        Assert.Equal(0, roslyn.InvalidateCount);
    }

    [Fact]
    public void Propose_replace_symbol_source_without_workspace_returns_workspace_unavailable()
    {
        using var temp = CreateRepo(("Parser.cs", "public sealed class Parser { }\n"));
        var service = new PatchTransactionService(temp.Path);

        var ex = Assert.Throws<PatchValidationException>(() => service.Propose(new ProposePatchRequest
        {
            Edits =
            [
                new PatchEditOperation
                {
                    Kind = "replace_symbol_source",
                    SymbolId = "T:Demo.Parser",
                    OldSourceHash = HashText("public sealed class Parser { }"),
                    NewText = "public sealed class Parser2 { }",
                },
            ],
        }));

        Assert.Equal("workspace_unavailable", ex.Code);
        Assert.Equal(0, ex.EditIndex);
        Assert.Equal("replace_symbol_source", ex.Kind);
    }

    [Fact]
    public void Propose_replace_symbol_source_maps_symbol_not_found()
    {
        using var temp = CreateRepo(("Parser.cs", "public sealed class Parser { }\n"));
        var roslyn = new FakeRoslynNavigationService(new SymbolNotFoundException("T:Demo.Missing"));
        var service = new PatchTransactionService(temp.Path, "TestRoot", roslynNavigation: roslyn);

        var ex = Assert.Throws<PatchValidationException>(() => service.Propose(new ProposePatchRequest
        {
            Edits =
            [
                new PatchEditOperation
                {
                    Kind = "replace_symbol_source",
                    SymbolId = "T:Demo.Missing",
                    OldSourceHash = HashText("public sealed class Parser { }"),
                    NewText = "public sealed class Parser2 { }",
                },
            ],
        }));

        Assert.Equal("semantic_symbol_not_found", ex.Code);
        Assert.Equal(0, ex.EditIndex);
        Assert.Equal("replace_symbol_source", ex.Kind);
    }

    [Fact]
    public void Propose_applies_files_before_edits()
    {
        using var temp = CreateRepo();
        var service = new PatchTransactionService(temp.Path);

        service.Propose(new ProposePatchRequest
        {
            Files =
            [
                new PatchFileOperation
                {
                    Path = "file.txt",
                    Operation = PatchFileOperationKind.Create,
                    NewContent = "created anchor",
                },
            ],
            Edits =
            [
                new PatchEditOperation
                {
                    Path = "file.txt",
                    Kind = "replace_exact",
                    OldText = "anchor",
                    NewText = "edited",
                },
            ],
        });

        Assert.Equal("created edited", File.ReadAllText(Path.Combine(temp.Path, "file.txt")));
    }

    [Fact]
    public void Propose_edit_zero_match_does_not_change_working_tree()
    {
        using var temp = CreateRepo(("file.txt", "one two three"));
        var filePath = Path.Combine(temp.Path, "file.txt");
        var service = new PatchTransactionService(temp.Path);

        var ex = Assert.Throws<PatchValidationException>(() => service.Propose(new ProposePatchRequest
        {
            Edits =
            [
                new PatchEditOperation
                {
                    Path = "file.txt",
                    Kind = "replace_exact",
                    OldText = "missing",
                    NewText = "new",
                },
            ],
        }));

        Assert.Equal("edit_anchor_not_found", ex.Code);
        Assert.Equal("file.txt", ex.Path);
        Assert.Equal(0, ex.EditIndex);
        Assert.Equal("replace_exact", ex.Kind);
        Assert.Equal(0, ex.MatchCount);
        Assert.Equal("one two three", File.ReadAllText(filePath));
        Assert.True(new LibGit2SharpGitStatusService(temp.Path).GetStatus().IsClean);
    }

    [Fact]
    public void Propose_anchor_not_found_reports_line_ending_hint_when_anchor_uses_lf_for_crlf_file()
    {
        using var temp = CreateRepo(("file.txt", "alpha\r\nbeta\r\n"));
        var service = new PatchTransactionService(temp.Path);

        var ex = Assert.Throws<PatchValidationException>(() => service.Propose(new ProposePatchRequest
        {
            Edits =
            [
                new PatchEditOperation
                {
                    Path = "file.txt",
                    Kind = "replace_exact",
                    OldText = "missing\n",
                    NewText = "new\n",
                },
            ],
        }));

        Assert.Equal("edit_anchor_not_found", ex.Code);
        Assert.Equal("file_uses_crlf_anchor_uses_lf", ex.LineEndingHint);
    }

    [Fact]
    public void Propose_anchor_not_found_does_not_report_line_ending_hint_for_escaped_newline_literal()
    {
        using var temp = CreateRepo(("file.cs", "public static class C\r\n{\r\n    const string NewLine = \"\\n\";\r\n}\r\n"));
        var service = new PatchTransactionService(temp.Path);

        var ex = Assert.Throws<PatchValidationException>(() => service.Propose(new ProposePatchRequest
        {
            Edits =
            [
                new PatchEditOperation
                {
                    Path = "file.cs",
                    Kind = "replace_exact",
                    OldText = "const string Missing = \"\\n\";",
                    NewText = "const string Missing = \"\\r\\n\";",
                },
            ],
        }));

        Assert.Equal("edit_anchor_not_found", ex.Code);
        Assert.Null(ex.LineEndingHint);
    }

    [Fact]
    public void Propose_edit_multi_match_does_not_change_working_tree()
    {
        using var temp = CreateRepo(("file.txt", "same same"));
        var filePath = Path.Combine(temp.Path, "file.txt");
        var service = new PatchTransactionService(temp.Path);

        var ex = Assert.Throws<PatchValidationException>(() => service.Propose(new ProposePatchRequest
        {
            Edits =
            [
                new PatchEditOperation
                {
                    Path = "file.txt",
                    Kind = "replace_exact",
                    OldText = "same",
                    NewText = "new",
                },
            ],
        }));

        Assert.Equal("edit_anchor_not_unique", ex.Code);
        Assert.Equal("file.txt", ex.Path);
        Assert.Equal(0, ex.EditIndex);
        Assert.Equal("replace_exact", ex.Kind);
        Assert.Equal(2, ex.MatchCount);
        Assert.Collection(ex.Matches!,
            match =>
            {
                Assert.Equal(1, match.Line);
                Assert.Equal(0, match.Column);
            },
            match =>
            {
                Assert.Equal(1, match.Line);
                Assert.Equal(5, match.Column);
            });
        Assert.Equal("same same", File.ReadAllText(filePath));
        Assert.True(new LibGit2SharpGitStatusService(temp.Path).GetStatus().IsClean);
    }

    [Fact]
    public void Propose_edit_multi_match_locations_are_capped()
    {
        using var temp = CreateRepo(("file.txt", string.Concat(Enumerable.Repeat("same\n", 25))));
        var service = new PatchTransactionService(temp.Path);

        var ex = Assert.Throws<PatchValidationException>(() => service.Propose(new ProposePatchRequest
        {
            Edits =
            [
                new PatchEditOperation
                {
                    Path = "file.txt",
                    Kind = "replace_exact",
                    OldText = "same\n",
                    NewText = "new\n",
                },
            ],
        }));

        Assert.Equal("edit_anchor_not_unique", ex.Code);
        Assert.Equal(25, ex.MatchCount);
        Assert.Equal(20, ex.Matches!.Count);
        Assert.Equal(new PatchEditMatchLocation(1, 0), ex.Matches[0]);
        Assert.Equal(new PatchEditMatchLocation(20, 0), ex.Matches[^1]);
    }

    [Fact]
    public void Propose_edit_expected_file_hash_mismatch_does_not_change_working_tree()
    {
        using var temp = CreateRepo(("file.txt", "old"));
        var filePath = Path.Combine(temp.Path, "file.txt");
        var service = new PatchTransactionService(temp.Path);

        var ex = Assert.Throws<PatchValidationException>(() => service.Propose(new ProposePatchRequest
        {
            Edits =
            [
                new PatchEditOperation
                {
                    Path = "file.txt",
                    Kind = "replace_exact",
                    OldText = "old",
                    NewText = "new",
                    ExpectedFileHash = "sha256:" + new string('0', 64),
                },
            ],
        }));

        Assert.Equal("edit_conflict", ex.Code);
        Assert.Equal("file.txt", ex.Path);
        Assert.Equal(0, ex.EditIndex);
        Assert.Equal("replace_exact", ex.Kind);
        Assert.Equal("expectedFileHash", ex.HashField);
        Assert.Equal("sha256:" + new string('0', 64), ex.ExpectedHash);
        Assert.Equal(ContentHash.ForFile(filePath), ex.ActualHash);
        Assert.Equal("file", ex.HashTarget);
        Assert.Equal("old", File.ReadAllText(filePath));
        Assert.True(new LibGit2SharpGitStatusService(temp.Path).GetStatus().IsClean);
    }

    [Fact]
    public void Propose_unsupported_edit_kind_returns_edit_details()
    {
        using var temp = CreateRepo(("file.txt", "old"));
        var service = new PatchTransactionService(temp.Path);

        var ex = Assert.Throws<PatchValidationException>(() => service.Propose(new ProposePatchRequest
        {
            Edits =
            [
                new PatchEditOperation
                {
                    Path = "file.txt",
                    Kind = "replace_regex",
                },
            ],
        }));

        Assert.Equal("unsupported_edit_kind", ex.Code);
        Assert.Equal("file.txt", ex.Path);
        Assert.Equal(0, ex.EditIndex);
        Assert.Equal("replace_regex", ex.Kind);
    }

    [Fact]
    public void Revert_after_accepted_patch_returns_not_active()
    {
        using var temp = CreateRepo(("file.txt", "old"));
        var filePath = Path.Combine(temp.Path, "file.txt");
        var service = new PatchTransactionService(temp.Path);
        var result = service.Propose(new ProposePatchRequest
        {
            Files =
            [
                new PatchFileOperation
                {
                    Path = "file.txt",
                    Operation = PatchFileOperationKind.Replace,
                    OldContentHash = ContentHash.ForFile(filePath),
                    NewContent = "new",
                },
                new PatchFileOperation
                {
                    Path = "created.txt",
                    Operation = PatchFileOperationKind.Create,
                    NewContent = "created",
                },
            ],
        });

        var ex = Assert.Throws<PatchValidationException>(() => service.Revert(result.PatchId!));

        Assert.Equal("patch_not_active", ex.Code);
        Assert.Equal("new", File.ReadAllText(filePath));
        Assert.True(File.Exists(Path.Combine(temp.Path, "created.txt")));
        Assert.Equal("none", service.Current().PatchStatus);
        Assert.False(new LibGit2SharpGitStatusService(temp.Path).GetStatus().IsClean);
    }

    [Fact]
    public void Propose_after_accepted_patch_is_rejected_by_dirty_working_tree()
    {
        using var temp = CreateRepo();
        var service = new PatchTransactionService(temp.Path);
        service.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "first.txt", Operation = PatchFileOperationKind.Create, NewContent = "first" }],
        });

        var ex = Assert.Throws<PatchValidationException>(() => service.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "second.txt", Operation = PatchFileOperationKind.Create, NewContent = "second" }],
        }));

        Assert.Equal("dirty_working_tree", ex.Code);
    }

    [Fact]
    public void Propose_with_successful_solution_build_accepts_and_stages_patch()
    {
        using var temp = CreateRepo(("ContextMessenger.slnx", "<Solution />"));
        var build = new FakeBuildRunner { Result = BuildResult("ok") };
        var tests = new FakeTestRunner();
        var service = new PatchTransactionService(temp.Path, "TestRoot", buildRunner: build, testRunner: tests);

        var result = service.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "new" }],
            Build = new PatchPolicy { Policy = "solution" },
            Tests = new PatchPolicy
            {
                Policy = "projects",
                Projects = ["ContextMessenger.slnx"],
                Filter = "FullyQualifiedName~Smoke",
            },
        });

        Assert.Equal("accepted", result.PatchStatus);
        Assert.Equal("ok", result.Build!.Status);
        Assert.Equal("ok", result.Tests!.Status);
        Assert.Equal("solution", result.Build.Policy);
        Assert.Equal("projects", result.Tests.Policy);
        Assert.Equal(3, result.Tests.TotalTests);
        Assert.Equal(3, result.Tests.ExecutedTests);
        Assert.Equal(3, result.Tests.PassedTests);
        Assert.Equal(0, result.Tests.FailedTests);
        Assert.Equal(0, result.Tests.SkippedTests);
        Assert.Equal("Debug", build.LastRequest!.Configuration);
        Assert.Equal(["ContextMessenger.slnx"], tests.LastRequest!.Projects);
        Assert.Equal("FullyQualifiedName~Smoke", tests.LastRequest.Filter);
        Assert.Equal("none", service.Current().PatchStatus);
        Assert.Contains(new LibGit2SharpGitStatusService(temp.Path).GetStatus().ChangedFiles,
            f => f is { Path: "new.txt", Status: "staged_new" });
    }

    [Fact]
    public void Propose_accepted_patch_invalidates_workspace()
    {
        using var temp = CreateRepo();
        var invalidator = new FakeWorkspaceInvalidator();
        var service = new PatchTransactionService(
            temp.Path,
            "TestRoot",
            workspaceInvalidator: invalidator);

        service.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "new" }],
        });

        Assert.Equal(1, invalidator.Count);
    }

    [Fact]
    public void Propose_with_failed_solution_build_persists_needs_revision()
    {
        using var temp = CreateRepo(("ContextMessenger.slnx", "<Solution />"));
        var store = new InMemoryPatchSessionStore();
        var build = new FakeBuildRunner
        {
            Result = BuildResult(
                "failed",
                diagnostics:
                [
                    new BuildDiagnostic
                    {
                        Kind = "error",
                        Code = "CS0103",
                        Path = "src/File.cs",
                        Line = 1,
                        Column = 2,
                        Message = "Missing name",
                    },
                ]),
        };
        var service = new PatchTransactionService(temp.Path, "TestRoot", store, build);

        var result = service.Propose(new ProposePatchRequest
        {
            Title = "Broken patch",
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "new" }],
            Build = new PatchPolicy { Policy = "solution" },
        });

        Assert.Equal("needs_revision", result.PatchStatus);
        Assert.Equal("build", result.LastFailureStage);
        Assert.Equal("failed", result.Build!.Status);
        Assert.Equal("CS0103", Assert.Single(result.Build.Diagnostics).Code);
        Assert.NotNull(store.Load());
        Assert.Equal("needs_revision", store.Load()!.Status);
        Assert.Equal("build", store.Load()!.LastFailureStage);

        var current = service.Current();
        Assert.Equal(result.PatchId, current.PatchId);
        Assert.Equal("needs_revision", current.PatchStatus);
        Assert.Contains(current.Files, f => f is { Path: "new.txt", Operation: "create" } && f.CurrentContentHash is not null);
        Assert.Contains(new LibGit2SharpGitStatusService(temp.Path).GetStatus().ChangedFiles,
            f => f is { Path: "new.txt", Status: "untracked" });

        var reverted = service.Revert(result.PatchId!);
        Assert.Equal("reverted", reverted.PatchStatus);
        Assert.Null(store.Load());
        Assert.True(new LibGit2SharpGitStatusService(temp.Path).GetStatus().IsClean);
    }

    [Fact]
    public void Propose_needs_revision_patch_invalidates_workspace()
    {
        using var temp = CreateRepo(("ContextMessenger.slnx", "<Solution />"));
        var invalidator = new FakeWorkspaceInvalidator();
        var build = new FakeBuildRunner { Result = BuildResult("failed") };
        var service = new PatchTransactionService(
            temp.Path,
            "TestRoot",
            buildRunner: build,
            workspaceInvalidator: invalidator);

        service.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "new" }],
            Build = new PatchPolicy { Policy = "solution" },
        });

        Assert.Equal(1, invalidator.Count);
    }

    [Fact]
    public void Amend_rejects_when_no_active_patch()
    {
        using var temp = CreateRepo();
        var service = new PatchTransactionService(temp.Path);

        var ex = Assert.Throws<PatchValidationException>(() => service.Amend(new AmendPatchRequest
        {
            PatchId = "p-missing",
            BaseRevision = 1,
            Files = [new PatchFileOperation { Path = "file.txt", Operation = PatchFileOperationKind.Create, NewContent = "new" }],
        }));

        Assert.Equal("patch_not_active", ex.Code);
    }

    [Fact]
    public void Amend_rejects_wrong_patch_id()
    {
        using var temp = CreateRepo(("ContextMessenger.slnx", "<Solution />"));
        var build = new QueueBuildRunner(BuildResult("failed"));
        var service = new PatchTransactionService(temp.Path, "TestRoot", buildRunner: build);
        var proposed = service.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "new" }],
            Build = new PatchPolicy { Policy = "solution" },
        });

        var ex = Assert.Throws<PatchValidationException>(() => service.Amend(new AmendPatchRequest
        {
            PatchId = "p-other",
            BaseRevision = proposed.Revision,
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Replace, OldContentHash = ContentHash.ForFile(Path.Combine(temp.Path, "new.txt")), NewContent = "fixed" }],
        }));

        Assert.Equal("patch_id_mismatch", ex.Code);
    }

    [Fact]
    public void Amend_rejects_wrong_base_revision()
    {
        using var temp = CreateRepo(("ContextMessenger.slnx", "<Solution />"));
        var build = new QueueBuildRunner(BuildResult("failed"));
        var service = new PatchTransactionService(temp.Path, "TestRoot", buildRunner: build);
        var proposed = service.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "new" }],
            Build = new PatchPolicy { Policy = "solution" },
        });

        var ex = Assert.Throws<PatchValidationException>(() => service.Amend(new AmendPatchRequest
        {
            PatchId = proposed.PatchId!,
            BaseRevision = proposed.Revision + 1,
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Replace, OldContentHash = ContentHash.ForFile(Path.Combine(temp.Path, "new.txt")), NewContent = "fixed" }],
        }));

        Assert.Equal("revision_mismatch", ex.Code);
    }

    [Fact]
    public void Amend_rejects_stale_hash()
    {
        using var temp = CreateRepo(("ContextMessenger.slnx", "<Solution />"));
        var build = new QueueBuildRunner(BuildResult("failed"));
        var service = new PatchTransactionService(temp.Path, "TestRoot", buildRunner: build);
        var proposed = service.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "new" }],
            Build = new PatchPolicy { Policy = "solution" },
        });

        var ex = Assert.Throws<PatchValidationException>(() => service.Amend(new AmendPatchRequest
        {
            PatchId = proposed.PatchId!,
            BaseRevision = proposed.Revision,
            Files =
            [
                new PatchFileOperation
                {
                    Path = "new.txt",
                    Operation = PatchFileOperationKind.Replace,
                    OldContentHash = "sha256:" + new string('0', 64),
                    NewContent = "fixed",
                },
            ],
        }));

        Assert.Equal("content_hash_mismatch", ex.Code);
        Assert.Equal("new", File.ReadAllText(Path.Combine(temp.Path, "new.txt")));
    }

    [Fact]
    public void Amend_apply_failure_restores_pre_amend_patch_state()
    {
        using var temp = CreateRepo(("ContextMessenger.slnx", "<Solution />"));
        var store = new InMemoryPatchSessionStore();
        var build = new QueueBuildRunner(BuildResult("failed"));
        var service = new PatchTransactionService(temp.Path, "TestRoot", store, build);
        var proposed = service.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "new" }],
            Build = new PatchPolicy { Policy = "solution" },
        });
        var filePath = Path.Combine(temp.Path, "new.txt");

        var ex = Assert.Throws<PatchValidationException>(() => service.Amend(new AmendPatchRequest
        {
            PatchId = proposed.PatchId!,
            BaseRevision = proposed.Revision,
            Files =
            [
                new PatchFileOperation
                {
                    Path = "new.txt",
                    Operation = PatchFileOperationKind.Create,
                    NewContent = "must not apply",
                },
            ],
        }));

        Assert.Equal("file_exists", ex.Code);
        Assert.Equal("new", File.ReadAllText(filePath));
        var current = service.Current();
        Assert.Equal("needs_revision", current.PatchStatus);
        Assert.Equal(proposed.Revision, current.Revision);
        Assert.Equal(proposed.PatchId, current.PatchId);
        Assert.Equal(proposed.Revision, store.Load()!.Revision);
    }

    [Fact]
    public void Propose_diff_verification_failure_restores_clean_working_tree()
    {
        using var temp = CreateRepo(("file.txt", "old"));
        var verifier = new QueuePatchDiffVerifier(new PatchValidationException("diff_verification_failed", "Forced diff failure."));
        var invalidator = new FakeWorkspaceInvalidator();
        var service = new PatchTransactionService(
            temp.Path,
            "TestRoot",
            workspaceInvalidator: invalidator,
            diffVerifier: verifier);

        var ex = Assert.Throws<PatchValidationException>(() => service.Propose(new ProposePatchRequest
        {
            Files =
            [
                new PatchFileOperation
                {
                    Path = "file.txt",
                    Operation = PatchFileOperationKind.Replace,
                    OldContentHash = ContentHash.ForFile(Path.Combine(temp.Path, "file.txt")),
                    NewContent = "new",
                },
            ],
        }));

        Assert.Equal("diff_verification_failed", ex.Code);
        Assert.Equal(1, verifier.Count);
        Assert.Equal("old", File.ReadAllText(Path.Combine(temp.Path, "file.txt")));
        Assert.Equal("none", service.Current().PatchStatus);
        Assert.True(new LibGit2SharpGitStatusService(temp.Path).GetStatus().IsClean);
        Assert.Equal(0, invalidator.Count);
    }

    [Fact]
    public void Amend_diff_verification_failure_restores_pre_amend_patch_state()
    {
        using var temp = CreateRepo(("ContextMessenger.slnx", "<Solution />"));
        var store = new InMemoryPatchSessionStore();
        var build = new QueueBuildRunner(BuildResult("failed"));
        var verifier = new QueuePatchDiffVerifier(null, new PatchValidationException("diff_verification_failed", "Forced diff failure."));
        var invalidator = new FakeWorkspaceInvalidator();
        var service = new PatchTransactionService(
            temp.Path,
            "TestRoot",
            store,
            build,
            workspaceInvalidator: invalidator,
            diffVerifier: verifier);
        var proposed = service.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "broken" }],
            Build = new PatchPolicy { Policy = "solution" },
        });
        var filePath = Path.Combine(temp.Path, "new.txt");

        var ex = Assert.Throws<PatchValidationException>(() => service.Amend(new AmendPatchRequest
        {
            PatchId = proposed.PatchId!,
            BaseRevision = proposed.Revision,
            Files =
            [
                new PatchFileOperation
                {
                    Path = "new.txt",
                    Operation = PatchFileOperationKind.Replace,
                    OldContentHash = ContentHash.ForFile(filePath),
                    NewContent = "fixed",
                },
            ],
        }));

        Assert.Equal("diff_verification_failed", ex.Code);
        Assert.Equal(2, verifier.Count);
        Assert.Equal("broken", File.ReadAllText(filePath));
        var current = service.Current();
        Assert.Equal("needs_revision", current.PatchStatus);
        Assert.Equal(proposed.Revision, current.Revision);
        Assert.Equal(proposed.PatchId, current.PatchId);
        Assert.Equal(proposed.Revision, store.Load()!.Revision);
        Assert.Equal(1, invalidator.Count);
    }

    [Fact]
    public void Validate_proposal_checks_operations_and_policies_without_applying()
    {
        using var temp = CreateRepo(("ContextMessenger.slnx", "<Solution />"), ("file.txt", "old"));
        var service = new PatchTransactionService(temp.Path);
        var filePath = Path.Combine(temp.Path, "file.txt");

        var result = service.Validate(new ValidatePatchRequest
        {
            Files =
            [
                new PatchFileOperation
                {
                    Path = "file.txt",
                    Operation = PatchFileOperationKind.Replace,
                    OldContentHash = ContentHash.ForFile(filePath),
                    NewContent = "new",
                },
            ],
            Build = new PatchPolicy { Policy = "solution" },
            Tests = new PatchPolicy { Policy = "none" },
        });

        Assert.True(result.Valid);
        Assert.Equal("propose", result.Mode);
        Assert.False(result.Applied);
        Assert.False(result.DiffVerified);
        Assert.Equal("validated", result.Build.Status);
        Assert.Equal("solution", result.Build.Policy);
        Assert.Equal("skipped", result.Tests.Status);
        Assert.Equal("old", File.ReadAllText(filePath));
        Assert.Equal("none", service.Current().PatchStatus);
        Assert.True(new LibGit2SharpGitStatusService(temp.Path).GetStatus().IsClean);
        Assert.Contains(result.Files, f => f is { Path: "file.txt", Operation: "replace", LastRevision: 0 });
    }

    [Fact]
    public void Validate_proposal_allows_dirty_working_tree_and_reports_warning()
    {
        using var temp = CreateRepo(("file.txt", "old"));
        var service = new PatchTransactionService(temp.Path);
        var filePath = Path.Combine(temp.Path, "file.txt");
        File.WriteAllText(filePath, "dirty");

        var result = service.Validate(new ValidatePatchRequest
        {
            Files =
            [
                new PatchFileOperation
                {
                    Path = "file.txt",
                    Operation = PatchFileOperationKind.Replace,
                    OldContentHash = ContentHash.ForFile(filePath),
                    NewContent = "candidate",
                },
            ],
        });

        Assert.True(result.Valid);
        Assert.Equal("propose", result.Mode);
        Assert.False(result.Applied);
        Assert.False(result.DiffVerified);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal("dirty_working_tree", warning.Code);
        Assert.Contains("propose_patch still requires a clean git working tree", warning.Message);
        Assert.Equal("dirty", File.ReadAllText(filePath));
        Assert.Equal("none", service.Current().PatchStatus);
        Assert.False(new LibGit2SharpGitStatusService(temp.Path).GetStatus().IsClean);
    }

    [Fact]
    public void Validate_proposal_edit_failure_does_not_apply_or_create_patch()
    {
        using var temp = CreateRepo(("file.txt", "alpha\nbeta\n"));
        var service = new PatchTransactionService(temp.Path);

        var ex = Assert.Throws<PatchValidationException>(() => service.Validate(new ValidatePatchRequest
        {
            Edits =
            [
                new PatchEditOperation
                {
                    Path = "file.txt",
                    Kind = "replace_exact",
                    OldText = "missing",
                    NewText = "new",
                },
            ],
        }));

        Assert.Equal("edit_anchor_not_found", ex.Code);
        Assert.Equal("alpha\nbeta\n", File.ReadAllText(Path.Combine(temp.Path, "file.txt")));
        Assert.Equal("none", service.Current().PatchStatus);
        Assert.True(new LibGit2SharpGitStatusService(temp.Path).GetStatus().IsClean);
    }

    [Fact]
    public void Validate_proposal_policy_failure_is_immediate_and_non_mutating()
    {
        using var temp = CreateRepo(("file.txt", "old"));
        var service = new PatchTransactionService(temp.Path);

        var ex = Assert.Throws<PatchValidationException>(() => service.Validate(new ValidatePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "new" }],
            Tests = new PatchPolicy { Policy = "filter", Filter = "FullyQualifiedName~Anything" },
        }));

        Assert.Equal("invalid_patch_policy", ex.Code);
        Assert.False(File.Exists(Path.Combine(temp.Path, "new.txt")));
        Assert.Equal("none", service.Current().PatchStatus);
        Assert.True(new LibGit2SharpGitStatusService(temp.Path).GetStatus().IsClean);
    }

    [Fact]
    public void Validate_amendment_uses_active_patch_state_and_inherits_policies_without_advancing()
    {
        using var temp = CreateRepo(("ContextMessenger.slnx", "<Solution />"));
        var store = new InMemoryPatchSessionStore();
        var build = new QueueBuildRunner(BuildResult("failed"));
        var service = new PatchTransactionService(temp.Path, "TestRoot", store, build);
        var proposed = service.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "broken" }],
            Build = new PatchPolicy { Policy = "solution" },
            Tests = new PatchPolicy { Policy = "none" },
        });
        var filePath = Path.Combine(temp.Path, "new.txt");

        var result = service.Validate(new ValidatePatchRequest
        {
            PatchId = proposed.PatchId,
            BaseRevision = proposed.Revision,
            Files =
            [
                new PatchFileOperation
                {
                    Path = "new.txt",
                    Operation = PatchFileOperationKind.Replace,
                    OldContentHash = ContentHash.ForFile(filePath),
                    NewContent = "fixed",
                },
            ],
        });

        Assert.True(result.Valid);
        Assert.Equal("amend", result.Mode);
        Assert.Equal(proposed.PatchId, result.PatchId);
        Assert.Equal(proposed.Revision, result.BaseRevision);
        Assert.Equal("validated", result.Build.Status);
        Assert.Equal("solution", result.Build.Policy);
        Assert.Equal("skipped", result.Tests.Status);
        Assert.Equal("broken", File.ReadAllText(filePath));
        Assert.Equal(proposed.Revision, service.Current().Revision);
        Assert.Equal(proposed.Revision, store.Load()!.Revision);
    }

    [Fact]
    public void Reply_only_amend_keeps_current_state_without_advancing_revision()
    {
        using var temp = CreateRepo(("ContextMessenger.slnx", "<Solution />"));
        var store = new InMemoryPatchSessionStore();
        var build = new QueueBuildRunner(BuildResult("failed"));
        var service = new PatchTransactionService(temp.Path, "TestRoot", store, build);
        var proposed = service.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "new" }],
            Build = new PatchPolicy { Policy = "solution" },
        });
        Assert.Equal("needs_revision", proposed.PatchStatus);

        // Empty files/edits is now a reply-only amend: no error, no revision advance, state preserved.
        var amended = service.Amend(new AmendPatchRequest
        {
            PatchId = proposed.PatchId!,
            BaseRevision = proposed.Revision,
        });

        Assert.Equal("needs_revision", amended.PatchStatus);
        Assert.Equal(proposed.Revision, amended.Revision);
        Assert.Equal(proposed.Revision, service.Current().Revision);
        Assert.Equal(proposed.Revision, store.Load()!.Revision);
    }

    [Fact]
    public void Reply_only_amend_resaves_active_patch_metadata()
    {
        using var temp = CreateRepo(("ContextMessenger.slnx", "<Solution />"));
        var store = new InMemoryPatchSessionStore();
        var build = new QueueBuildRunner(BuildResult("failed"));
        var service = new PatchTransactionService(temp.Path, "TestRoot", store, build);
        var proposed = service.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "new" }],
            Build = new PatchPolicy { Policy = "solution" },
        });
        store.Clear();

        service.Amend(new AmendPatchRequest
        {
            PatchId = proposed.PatchId!,
            BaseRevision = proposed.Revision,
        });

        var metadata = store.Load();
        Assert.NotNull(metadata);
        Assert.Equal(proposed.PatchId, metadata.PatchId);
        Assert.Equal(proposed.Revision, metadata.Revision);
        Assert.Equal("needs_revision", metadata.Status);
    }

    [Fact]
    public void Amend_after_failed_build_can_fix_and_accept_patch()
    {
        using var temp = CreateRepo(("ContextMessenger.slnx", "<Solution />"));
        var store = new InMemoryPatchSessionStore();
        var build = new QueueBuildRunner(BuildResult("failed"), BuildResult("ok"));
        var service = new PatchTransactionService(temp.Path, "TestRoot", store, build);

        var proposed = service.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "broken" }],
            Build = new PatchPolicy { Policy = "solution" },
        });
        var filePath = Path.Combine(temp.Path, "new.txt");

        var amended = service.Amend(new AmendPatchRequest
        {
            PatchId = proposed.PatchId!,
            BaseRevision = proposed.Revision,
            Files =
            [
                new PatchFileOperation
                {
                    Path = "new.txt",
                    Operation = PatchFileOperationKind.Replace,
                    OldContentHash = ContentHash.ForFile(filePath),
                    NewContent = "fixed",
                },
            ],
        });

        Assert.Equal("accepted", amended.PatchStatus);
        Assert.Equal(2, amended.Revision);
        Assert.Equal("fixed", File.ReadAllText(filePath));
        Assert.Equal("none", service.Current().PatchStatus);
        Assert.Null(store.Load());
        Assert.Contains(new LibGit2SharpGitStatusService(temp.Path).GetStatus().ChangedFiles,
            f => f is { Path: "new.txt", Status: "staged_new" });
    }

    [Fact]
    public void Amend_with_multiple_edits_increments_revision_once()
    {
        using var temp = CreateRepo(("ContextMessenger.slnx", "<Solution />"));
        var store = new InMemoryPatchSessionStore();
        var build = new QueueBuildRunner(BuildResult("failed"), BuildResult("failed"));
        var service = new PatchTransactionService(temp.Path, "TestRoot", store, build);
        var proposed = service.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "one two three" }],
            Build = new PatchPolicy { Policy = "solution" },
        });

        var amended = service.Amend(new AmendPatchRequest
        {
            PatchId = proposed.PatchId!,
            BaseRevision = proposed.Revision,
            Edits =
            [
                new PatchEditOperation
                {
                    Path = "new.txt",
                    Kind = "replace_exact",
                    OldText = "one",
                    NewText = "ONE",
                },
                new PatchEditOperation
                {
                    Path = "new.txt",
                    Kind = "replace_exact",
                    OldText = "three",
                    NewText = "THREE",
                },
            ],
        });

        Assert.Equal("needs_revision", amended.PatchStatus);
        Assert.Equal(2, amended.Revision);
        Assert.Equal("ONE two THREE", File.ReadAllText(Path.Combine(temp.Path, "new.txt")));
    }

    [Fact]
    public void Amend_edit_failure_leaves_active_patch_unchanged()
    {
        using var temp = CreateRepo(("ContextMessenger.slnx", "<Solution />"));
        var store = new InMemoryPatchSessionStore();
        var build = new QueueBuildRunner(BuildResult("failed"));
        var service = new PatchTransactionService(temp.Path, "TestRoot", store, build);
        var proposed = service.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "one two three" }],
            Build = new PatchPolicy { Policy = "solution" },
        });

        var ex = Assert.Throws<PatchValidationException>(() => service.Amend(new AmendPatchRequest
        {
            PatchId = proposed.PatchId!,
            BaseRevision = proposed.Revision,
            Edits =
            [
                new PatchEditOperation
                {
                    Path = "new.txt",
                    Kind = "replace_exact",
                    OldText = "one",
                    NewText = "ONE",
                },
                new PatchEditOperation
                {
                    Path = "new.txt",
                    Kind = "replace_exact",
                    OldText = "missing",
                    NewText = "MISSING",
                },
            ],
        }));

        Assert.Equal("edit_anchor_not_found", ex.Code);
        Assert.Equal("one two three", File.ReadAllText(Path.Combine(temp.Path, "new.txt")));
        var current = service.Current();
        Assert.Equal("needs_revision", current.PatchStatus);
        Assert.Equal(proposed.Revision, current.Revision);
        Assert.Equal(proposed.PatchId, current.PatchId);
        Assert.Equal(proposed.Revision, store.Load()!.Revision);
    }

    [Fact]
    public void Amend_invalidates_workspace()
    {
        using var temp = CreateRepo(("ContextMessenger.slnx", "<Solution />"));
        var invalidator = new FakeWorkspaceInvalidator();
        var build = new QueueBuildRunner(BuildResult("failed"), BuildResult("ok"));
        var service = new PatchTransactionService(
            temp.Path,
            "TestRoot",
            buildRunner: build,
            workspaceInvalidator: invalidator);
        var proposed = service.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "broken" }],
            Build = new PatchPolicy { Policy = "solution" },
        });

        service.Amend(new AmendPatchRequest
        {
            PatchId = proposed.PatchId!,
            BaseRevision = proposed.Revision,
            Files =
            [
                new PatchFileOperation
                {
                    Path = "new.txt",
                    Operation = PatchFileOperationKind.Replace,
                    OldContentHash = ContentHash.ForFile(Path.Combine(temp.Path, "new.txt")),
                    NewContent = "fixed",
                },
            ],
        });

        Assert.Equal(2, invalidator.Count);
    }

    [Fact]
    public void Amend_that_still_fails_remains_needs_revision()
    {
        using var temp = CreateRepo(("ContextMessenger.slnx", "<Solution />"));
        var store = new InMemoryPatchSessionStore();
        var build = new QueueBuildRunner(BuildResult("failed"), BuildResult("failed"));
        var service = new PatchTransactionService(temp.Path, "TestRoot", store, build);
        var proposed = service.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "broken" }],
            Build = new PatchPolicy { Policy = "solution" },
        });
        var filePath = Path.Combine(temp.Path, "new.txt");

        var amended = service.Amend(new AmendPatchRequest
        {
            PatchId = proposed.PatchId!,
            BaseRevision = proposed.Revision,
            Files =
            [
                new PatchFileOperation
                {
                    Path = "new.txt",
                    Operation = PatchFileOperationKind.Replace,
                    OldContentHash = ContentHash.ForFile(filePath),
                    NewContent = "still broken",
                },
            ],
        });

        Assert.Equal("needs_revision", amended.PatchStatus);
        Assert.Equal(2, amended.Revision);
        Assert.Equal("build", amended.LastFailureStage);
        Assert.Equal("needs_revision", store.Load()!.Status);
        Assert.Equal(2, store.Load()!.Revision);
        Assert.Equal("still broken", File.ReadAllText(filePath));
    }

    [Fact]
    public void Revert_after_amend_restores_original_state()
    {
        using var temp = CreateRepo(("ContextMessenger.slnx", "<Solution />"), ("file.txt", "old"));
        var filePath = Path.Combine(temp.Path, "file.txt");
        var build = new QueueBuildRunner(BuildResult("failed"), BuildResult("failed"));
        var service = new PatchTransactionService(temp.Path, "TestRoot", buildRunner: build);
        var proposed = service.Propose(new ProposePatchRequest
        {
            Files =
            [
                new PatchFileOperation
                {
                    Path = "file.txt",
                    Operation = PatchFileOperationKind.Replace,
                    OldContentHash = ContentHash.ForFile(filePath),
                    NewContent = "broken",
                },
                new PatchFileOperation
                {
                    Path = "created.txt",
                    Operation = PatchFileOperationKind.Create,
                    NewContent = "created",
                },
            ],
            Build = new PatchPolicy { Policy = "solution" },
        });

        var amended = service.Amend(new AmendPatchRequest
        {
            PatchId = proposed.PatchId!,
            BaseRevision = proposed.Revision,
            Files =
            [
                new PatchFileOperation
                {
                    Path = "file.txt",
                    Operation = PatchFileOperationKind.Replace,
                    OldContentHash = ContentHash.ForFile(filePath),
                    NewContent = "still broken",
                },
            ],
        });

        var reverted = service.Revert(amended.PatchId!);

        Assert.Equal("reverted", reverted.PatchStatus);
        Assert.Equal("old", File.ReadAllText(filePath));
        Assert.False(File.Exists(Path.Combine(temp.Path, "created.txt")));
        Assert.True(new LibGit2SharpGitStatusService(temp.Path).GetStatus().IsClean);
    }

    [Fact]
    public void Revert_invalidates_workspace()
    {
        using var temp = CreateRepo(("ContextMessenger.slnx", "<Solution />"));
        var invalidator = new FakeWorkspaceInvalidator();
        var build = new QueueBuildRunner(BuildResult("failed"));
        var service = new PatchTransactionService(
            temp.Path,
            "TestRoot",
            buildRunner: build,
            workspaceInvalidator: invalidator);
        var proposed = service.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "broken" }],
            Build = new PatchPolicy { Policy = "solution" },
        });

        service.Revert(proposed.PatchId!);

        Assert.Equal(2, invalidator.Count);
    }

    [Fact]
    public void Revert_refuses_when_head_moved_off_patch_base()
    {
        using var temp = CreateRepo(("ContextMessenger.slnx", "<Solution />"));
        var build = new QueueBuildRunner(BuildResult("failed"));
        var service = new PatchTransactionService(temp.Path, "TestRoot", buildRunner: build);
        var proposed = service.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "broken" }],
            Build = new PatchPolicy { Policy = "solution" },
        });
        Assert.Equal("needs_revision", proposed.PatchStatus);

        // Simulate the user committing outside the app, moving HEAD off the patch base commit.
        using (var repo = new Repository(temp.Path))
        {
            Commands.Stage(repo, "*");
            repo.Commit("external commit", Signature(), Signature());
        }

        var ex = Assert.Throws<PatchValidationException>(() => service.Revert(proposed.PatchId!));
        Assert.Equal("invalid_git_state", ex.Code);

        // The patch is still active; HEAD and the intervening commit were left untouched.
        Assert.Equal("needs_revision", service.Current().PatchStatus);
        using var verify = new Repository(temp.Path);
        Assert.Equal("external commit", verify.Head.Tip!.MessageShort);
    }

    [Fact]
    public void Concurrent_propose_calls_resolve_to_a_single_active_patch()
    {
        using var temp = CreateRepo(("ContextMessenger.slnx", "<Solution />"));
        // A failing build keeps the first proposal active (needs_revision), so every later
        // proposal must observe it and be rejected rather than racing into the apply path.
        var build = new FakeBuildRunner { Result = BuildResult("failed") };
        var service = new PatchTransactionService(temp.Path, "TestRoot", buildRunner: build);

        var successes = 0;
        var inProgressRejections = 0;
        Parallel.For(0, 8, _ =>
        {
            try
            {
                service.Propose(new ProposePatchRequest
                {
                    Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "x" }],
                    Build = new PatchPolicy { Policy = "solution" },
                });
                Interlocked.Increment(ref successes);
            }
            catch (PatchValidationException ex) when (ex.Code == "patch_in_progress")
            {
                Interlocked.Increment(ref inProgressRejections);
            }
        });

        Assert.Equal(1, successes);
        Assert.Equal(7, inProgressRejections);
        Assert.Equal("needs_revision", service.Current().PatchStatus);
    }

    [Fact]
    public void Recovery_clears_stale_metadata_when_repository_is_clean()
    {
        using var temp = CreateRepo(("file.txt", "old"));
        var store = new InMemoryPatchSessionStore(Metadata(temp.Path));

        var service = new PatchTransactionService(temp.Path, "TestRoot", store);

        Assert.Equal("none", service.Current().PatchStatus);
        Assert.Null(store.Load());
    }

    [Fact]
    public void Recovery_reconstructs_active_patch_from_dirty_repository()
    {
        using var temp = CreateRepo(("file.txt", "old"));
        temp.CreateFile("file.txt", "new");
        temp.CreateFile("created.txt", "created");
        var store = new InMemoryPatchSessionStore(Metadata(temp.Path, lastFailureStage: "build"));

        var service = new PatchTransactionService(temp.Path, "TestRoot", store);
        var current = service.Current();

        Assert.Equal("needs_revision", current.PatchStatus);
        Assert.True(current.Recovered);
        Assert.Equal("build", current.LastFailureStage);
        Assert.Contains(current.Files, f => f is { Path: "file.txt", Operation: "replace" } && f.CurrentContentHash is not null);
        Assert.Contains(current.Files, f => f is { Path: "created.txt", Operation: "create" } && f.CurrentContentHash is not null);
    }

    [Fact]
    public void Revert_recovered_patch_restores_base_and_clears_metadata()
    {
        using var temp = CreateRepo(("file.txt", "old"));
        temp.CreateFile("file.txt", "new");
        temp.CreateFile("created.txt", "created");
        var store = new InMemoryPatchSessionStore(Metadata(temp.Path));
        var service = new PatchTransactionService(temp.Path, "TestRoot", store);

        var reverted = service.Revert("p-recovered");

        Assert.Equal("reverted", reverted.PatchStatus);
        Assert.True(reverted.Recovered);
        Assert.Equal("old", File.ReadAllText(Path.Combine(temp.Path, "file.txt")));
        Assert.False(File.Exists(Path.Combine(temp.Path, "created.txt")));
        Assert.True(new LibGit2SharpGitStatusService(temp.Path).GetStatus().IsClean);
        Assert.Null(store.Load());
    }

    [Fact]
    public void Dirty_repository_without_metadata_does_not_create_active_patch()
    {
        using var temp = CreateRepo(("file.txt", "old"));
        temp.CreateFile("file.txt", "new");
        var service = new PatchTransactionService(temp.Path, "TestRoot", new InMemoryPatchSessionStore());

        Assert.Equal("none", service.Current().PatchStatus);
    }

    [Fact]
    public void Propose_with_unknown_test_policy_persists_needs_revision()
    {
        using var temp = CreateRepo();
        var store = new InMemoryPatchSessionStore();
        var service = new PatchTransactionService(temp.Path, "TestRoot", store);

        var result = service.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "new" }],
            Tests = new PatchPolicy { Policy = "affected_tests" },
        });

        Assert.Equal("needs_revision", result.PatchStatus);
        Assert.Equal("tests", result.LastFailureStage);
        Assert.Equal("failed", result.Tests!.Status);
        Assert.Equal("unsupported_patch_policy", Assert.Single(result.Tests.Diagnostics).Code);
        Assert.Equal("needs_revision", store.Load()!.Status);
        Assert.Equal(result.PatchId, service.Current().PatchId);
        Assert.Contains(new LibGit2SharpGitStatusService(temp.Path).GetStatus().ChangedFiles,
            f => f is { Path: "new.txt", Status: "untracked" });
    }

    [Fact]
    public void Propose_with_failed_tests_persists_needs_revision()
    {
        using var temp = CreateRepo(("ContextMessenger.slnx", "<Solution />"));
        var store = new InMemoryPatchSessionStore();
        var build = new FakeBuildRunner { Result = BuildResult("ok") };
        var tests = new FakeTestRunner
        {
            Result = TestResult(
                "failed",
                diagnostics:
                [
                    new BuildDiagnostic
                    {
                        Kind = "test",
                        Code = "ProbeTests.Fails",
                        Message = "Expected true but was false.",
                    },
                ]),
        };
        var service = new PatchTransactionService(temp.Path, "TestRoot", store, build, tests);

        var result = service.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "new" }],
            Build = new PatchPolicy { Policy = "solution" },
            Tests = new PatchPolicy { Policy = "all" },
        });

        Assert.Equal("needs_revision", result.PatchStatus);
        Assert.Equal("tests", result.LastFailureStage);
        Assert.Equal("ok", result.Build!.Status);
        Assert.Equal("failed", result.Tests!.Status);
        Assert.Equal(2, result.Tests.TotalTests);
        Assert.Equal(2, result.Tests.ExecutedTests);
        Assert.Equal(1, result.Tests.PassedTests);
        Assert.Equal(1, result.Tests.FailedTests);
        Assert.Equal(0, result.Tests.SkippedTests);
        Assert.Equal("ProbeTests.Fails", Assert.Single(result.Tests.Diagnostics).Code);
        Assert.Equal("tests", store.Load()!.LastFailureStage);
        Assert.Contains(new LibGit2SharpGitStatusService(temp.Path).GetStatus().ChangedFiles,
            f => f is { Path: "new.txt", Status: "untracked" });
    }

    [Fact]
    public void Amend_after_failed_tests_can_fix_and_accept_patch()
    {
        using var temp = CreateRepo(("ContextMessenger.slnx", "<Solution />"));
        var store = new InMemoryPatchSessionStore();
        var build = new FakeBuildRunner { Result = BuildResult("ok") };
        var tests = new QueueTestRunner(TestResult("failed"), TestResult("ok"));
        var service = new PatchTransactionService(temp.Path, "TestRoot", store, build, tests);
        var proposed = service.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "broken" }],
            Build = new PatchPolicy { Policy = "solution" },
            Tests = new PatchPolicy { Policy = "all" },
        });
        var filePath = Path.Combine(temp.Path, "new.txt");

        var amended = service.Amend(new AmendPatchRequest
        {
            PatchId = proposed.PatchId!,
            BaseRevision = proposed.Revision,
            Files =
            [
                new PatchFileOperation
                {
                    Path = "new.txt",
                    Operation = PatchFileOperationKind.Replace,
                    OldContentHash = ContentHash.ForFile(filePath),
                    NewContent = "fixed",
                },
            ],
        });

        Assert.Equal("accepted", amended.PatchStatus);
        Assert.Equal("ok", amended.Tests!.Status);
        Assert.Equal("fixed", File.ReadAllText(filePath));
        Assert.Null(store.Load());
        Assert.Contains(new LibGit2SharpGitStatusService(temp.Path).GetStatus().ChangedFiles,
            f => f is { Path: "new.txt", Status: "staged_new" });
    }

    [Fact]
    public void Amend_with_invalid_test_policy_persists_needs_revision_after_applying_amendment()
    {
        using var temp = CreateRepo(("ContextMessenger.slnx", "<Solution />"));
        var store = new InMemoryPatchSessionStore();
        var build = new FakeBuildRunner { Result = BuildResult("ok") };
        var tests = new QueueTestRunner(TestResult("failed"));
        var service = new PatchTransactionService(temp.Path, "TestRoot", store, build, tests);
        var proposed = service.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "broken" }],
            Build = new PatchPolicy { Policy = "solution" },
            Tests = new PatchPolicy { Policy = "all" },
        });
        var filePath = Path.Combine(temp.Path, "new.txt");

        var amended = service.Amend(new AmendPatchRequest
        {
            PatchId = proposed.PatchId!,
            BaseRevision = proposed.Revision,
            Files =
            [
                new PatchFileOperation
                {
                    Path = "new.txt",
                    Operation = PatchFileOperationKind.Replace,
                    OldContentHash = ContentHash.ForFile(filePath),
                    NewContent = "fixed",
                },
            ],
            Tests = new PatchPolicy { Policy = "filter", Projects = ["ContextMessenger.slnx"] },
        });

        Assert.Equal("needs_revision", amended.PatchStatus);
        Assert.Equal(2, amended.Revision);
        Assert.Equal("tests", amended.LastFailureStage);
        Assert.Equal("ok", amended.Build!.Status);
        Assert.Equal("failed", amended.Tests!.Status);
        Assert.Equal("invalid_patch_policy", Assert.Single(amended.Tests.Diagnostics).Code);
        Assert.Equal("fixed", File.ReadAllText(filePath));
        Assert.Equal(2, store.Load()!.Revision);
        Assert.Equal("needs_revision", service.Current().PatchStatus);
    }

    [Fact]
    public void Propose_with_filter_tests_without_filter_persists_needs_revision()
    {
        using var temp = CreateRepo(("ContextMessenger.slnx", "<Solution />"));
        var store = new InMemoryPatchSessionStore();
        var service = new PatchTransactionService(temp.Path, "TestRoot", store);

        var result = service.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "new" }],
            Tests = new PatchPolicy { Policy = "filter", Projects = ["ContextMessenger.slnx"] },
        });

        Assert.Equal("needs_revision", result.PatchStatus);
        Assert.Equal("tests", result.LastFailureStage);
        Assert.Equal("failed", result.Tests!.Status);
        Assert.Equal("invalid_patch_policy", Assert.Single(result.Tests.Diagnostics).Code);
        Assert.Equal("needs_revision", store.Load()!.Status);
        Assert.Equal(result.PatchId, service.Current().PatchId);
    }

    [Fact]
    public void Propose_with_unknown_build_policy_persists_needs_revision()
    {
        using var temp = CreateRepo();
        var store = new InMemoryPatchSessionStore();
        var service = new PatchTransactionService(temp.Path, "TestRoot", store);

        var result = service.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "new" }],
            Build = new PatchPolicy { Policy = "project" },
        });

        Assert.Equal("needs_revision", result.PatchStatus);
        Assert.Equal("build", result.LastFailureStage);
        Assert.Equal("failed", result.Build!.Status);
        Assert.Equal("unsupported_patch_policy", Assert.Single(result.Build.Diagnostics).Code);
        Assert.Equal("skipped", result.Tests!.Status);
        Assert.Equal("needs_revision", store.Load()!.Status);
        Assert.Equal(result.PatchId, service.Current().PatchId);
    }

    [Fact]
    public void Propose_rejected_before_apply_does_not_invalidate_workspace()
    {
        using var temp = CreateRepo(("file.txt", "old"));
        var invalidator = new FakeWorkspaceInvalidator();
        var service = new PatchTransactionService(
            temp.Path,
            "TestRoot",
            workspaceInvalidator: invalidator);

        var ex = Assert.Throws<PatchValidationException>(() => service.Propose(new ProposePatchRequest
        {
            Files =
            [
                new PatchFileOperation
                {
                    Path = "file.txt",
                    Operation = PatchFileOperationKind.Replace,
                    OldContentHash = "sha256:" + new string('0', 64),
                    NewContent = "new",
                },
            ],
        }));

        Assert.Equal("content_hash_mismatch", ex.Code);
        Assert.Equal(0, invalidator.Count);
    }

    // ---- Pass 0: deferred acceptance (hold-for-review mechanism) ----

    [Fact]
    public void Propose_with_defer_acceptance_holds_unstaged_and_keeps_transaction_open()
    {
        using var temp = CreateRepo(("replace.txt", "old"), ("delete.txt", "delete"));
        var replacePath = Path.Combine(temp.Path, "replace.txt");
        var deletePath = Path.Combine(temp.Path, "delete.txt");
        var store = new InMemoryPatchSessionStore();
        var service = new PatchTransactionService(temp.Path, "TestRoot", store);

        var result = service.Propose(new ProposePatchRequest
        {
            Title = "Held patch",
            DeferAcceptance = true,
            Files =
            [
                new PatchFileOperation
                {
                    Path = "replace.txt",
                    Operation = PatchFileOperationKind.Replace,
                    OldContentHash = ContentHash.ForFile(replacePath),
                    NewContent = "new",
                },
                new PatchFileOperation
                {
                    Path = "create.txt",
                    Operation = PatchFileOperationKind.Create,
                    NewContent = "created",
                },
                new PatchFileOperation
                {
                    Path = "delete.txt",
                    Operation = PatchFileOperationKind.Delete,
                    OldContentHash = ContentHash.ForFile(deletePath),
                },
            ],
        });

        // Held, not accepted.
        Assert.Equal("awaiting_acceptance", result.PatchStatus);
        Assert.True(result.Applied);
        Assert.True(result.DiffVerified);
        Assert.NotNull(result.PatchId);

        // Files applied to the working tree.
        Assert.Equal("new", File.ReadAllText(replacePath));
        Assert.Equal("created", File.ReadAllText(Path.Combine(temp.Path, "create.txt")));
        Assert.False(File.Exists(deletePath));

        // But NOT staged — changes live only in the working tree.
        var status = new LibGit2SharpGitStatusService(temp.Path).GetStatus();
        Assert.False(status.IsClean);
        Assert.Contains(status.ChangedFiles, f => f is { Path: "replace.txt", Status: "modified_unstaged" });
        Assert.Contains(status.ChangedFiles, f => f is { Path: "create.txt", Status: "untracked" });
        Assert.Contains(status.ChangedFiles, f => f is { Path: "delete.txt", Status: "deleted_unstaged" });

        // Transaction stays open and is persisted for recovery.
        var current = service.Current();
        Assert.Equal("awaiting_acceptance", current.PatchStatus);
        Assert.Equal(result.PatchId, current.PatchId);
        Assert.Equal("awaiting_acceptance", store.Load()!.Status);
    }

    [Fact]
    public void Accept_stages_deferred_patch_and_closes_transaction()
    {
        using var temp = CreateRepo(("replace.txt", "old"));
        var replacePath = Path.Combine(temp.Path, "replace.txt");
        var store = new InMemoryPatchSessionStore();
        var service = new PatchTransactionService(temp.Path, "TestRoot", store);

        var held = service.Propose(new ProposePatchRequest
        {
            DeferAcceptance = true,
            Files =
            [
                new PatchFileOperation
                {
                    Path = "replace.txt",
                    Operation = PatchFileOperationKind.Replace,
                    OldContentHash = ContentHash.ForFile(replacePath),
                    NewContent = "new",
                },
                new PatchFileOperation { Path = "create.txt", Operation = PatchFileOperationKind.Create, NewContent = "created" },
            ],
        });
        Assert.Equal("awaiting_acceptance", held.PatchStatus);

        var accepted = service.Accept(held.PatchId!);

        Assert.Equal("accepted", accepted.PatchStatus);
        Assert.Equal(held.PatchId, accepted.PatchId);

        // Now staged and the transaction is closed.
        Assert.Equal("none", service.Current().PatchStatus);
        Assert.Null(store.Load());
        var status = new LibGit2SharpGitStatusService(temp.Path).GetStatus();
        Assert.Contains(status.ChangedFiles, f => f is { Path: "replace.txt", Status: "staged_modified" });
        Assert.Contains(status.ChangedFiles, f => f is { Path: "create.txt", Status: "staged_new" });
    }

    [Fact]
    public void Accept_reports_the_validated_build_and_test_results_not_skipped()
    {
        using var temp = CreateRepo(("ContextMessenger.slnx", "<Solution />"));
        var build = new FakeBuildRunner { Result = BuildResult("ok") };
        var tests = new FakeTestRunner();
        var service = new PatchTransactionService(temp.Path, "TestRoot", buildRunner: build, testRunner: tests)
        {
            DeferAcceptanceByDefault = true,
        };

        var held = service.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "new" }],
            Build = new PatchPolicy { Policy = "solution" },
            Tests = new PatchPolicy { Policy = "projects", Projects = ["ContextMessenger.slnx"], Filter = "FullyQualifiedName~Smoke" },
        });
        Assert.Equal("awaiting_acceptance", held.PatchStatus);
        Assert.Equal("ok", held.Build!.Status);

        var accepted = service.Accept(held.PatchId!);

        Assert.Equal("accepted", accepted.PatchStatus);
        // Before the fix these were reported as "skipped" even though the patch was validated.
        Assert.Equal("ok", accepted.Build!.Status);
        Assert.Equal("solution", accepted.Build.Policy);
        Assert.Equal("ok", accepted.Tests!.Status);
    }

    [Fact]
    public void Reply_only_amend_on_validated_patch_keeps_state_and_does_not_rerun_checks()
    {
        using var temp = CreateRepo(("ContextMessenger.slnx", "<Solution />"));
        var build = new FakeBuildRunner { Result = BuildResult("ok") };
        var tests = new FakeTestRunner();
        var service = new PatchTransactionService(temp.Path, "TestRoot", buildRunner: build, testRunner: tests)
        {
            DeferAcceptanceByDefault = true,
        };

        var held = service.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "new" }],
            Build = new PatchPolicy { Policy = "solution" },
            Tests = new PatchPolicy { Policy = "projects", Projects = ["ContextMessenger.slnx"], Filter = "FullyQualifiedName~Smoke" },
        });
        Assert.Equal("awaiting_acceptance", held.PatchStatus);
        Assert.Equal(1, build.Runs);
        Assert.Equal(1, tests.Runs);

        // Reply-only amend (no files/edits) on the validated patch.
        var amended = service.Amend(new AmendPatchRequest { PatchId = held.PatchId!, BaseRevision = held.Revision });

        Assert.Equal("awaiting_acceptance", amended.PatchStatus); // state unchanged
        Assert.Equal(held.Revision, amended.Revision);            // revision unchanged
        Assert.Equal("ok", amended.Build!.Status);                // reports validated result, not skipped
        Assert.Equal(1, build.Runs);                              // build not rerun
        Assert.Equal(1, tests.Runs);                              // tests not rerun
    }

    [Fact]
    public void Recovered_reply_only_amend_preserves_validated_results_without_rerunning_checks()
    {
        using var temp = CreateRepo(("ContextMessenger.slnx", "<Solution />"));
        var store = new InMemoryPatchSessionStore();
        var initialBuild = new FakeBuildRunner { Result = BuildResult("ok") };
        var initialTests = new FakeTestRunner();
        var initial = new PatchTransactionService(temp.Path, "TestRoot", store, initialBuild, initialTests)
        {
            DeferAcceptanceByDefault = true,
        };

        var held = initial.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "new" }],
            Build = new PatchPolicy { Policy = "solution" },
            Tests = new PatchPolicy { Policy = "projects", Projects = ["ContextMessenger.slnx"], Filter = "FullyQualifiedName~Smoke" },
        });
        Assert.Equal("awaiting_acceptance", held.PatchStatus);

        var recoveredBuild = new FakeBuildRunner { Result = BuildResult("failed") };
        var recoveredTests = new FakeTestRunner { Result = TestResult("failed") };
        var recovered = new PatchTransactionService(temp.Path, "TestRoot", store, recoveredBuild, recoveredTests)
        {
            DeferAcceptanceByDefault = true,
        };

        var amended = recovered.Amend(new AmendPatchRequest { PatchId = held.PatchId!, BaseRevision = held.Revision });

        Assert.True(amended.Recovered);
        Assert.Equal("awaiting_acceptance", amended.PatchStatus);
        Assert.Equal("ok", amended.Build!.Status);
        Assert.Equal("ok", amended.Tests!.Status);
        Assert.Equal(0, recoveredBuild.Runs);
        Assert.Equal(0, recoveredTests.Runs);

        var accepted = recovered.Accept(held.PatchId!);
        Assert.Equal("accepted", accepted.PatchStatus);
        Assert.Equal("ok", accepted.Build!.Status);
        Assert.Equal("ok", accepted.Tests!.Status);
    }

    [Fact]
    public void Amend_with_files_on_validated_patch_reopens_and_revalidates()
    {
        using var temp = CreateRepo(("ContextMessenger.slnx", "<Solution />"));
        var build = new QueueBuildRunner(BuildResult("ok"), BuildResult("failed"));
        var service = new PatchTransactionService(temp.Path, "TestRoot", buildRunner: build)
        {
            DeferAcceptanceByDefault = true,
        };

        var held = service.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "new" }],
            Build = new PatchPolicy { Policy = "solution" },
        });
        Assert.Equal("awaiting_acceptance", held.PatchStatus);

        var amended = service.Amend(new AmendPatchRequest
        {
            PatchId = held.PatchId!,
            BaseRevision = held.Revision,
            Files = [new PatchFileOperation { Path = "extra.txt", Operation = PatchFileOperationKind.Create, NewContent = "extra" }],
        });

        Assert.Equal("needs_revision", amended.PatchStatus);  // file change re-opened and re-ran the build (failed)
        Assert.Equal(held.Revision + 1, amended.Revision);    // file change bumped the revision
    }

    [Fact]
    public void Accept_without_active_patch_throws_patch_not_active()
    {
        using var temp = CreateRepo(("file.txt", "old"));
        var service = new PatchTransactionService(temp.Path, "TestRoot");

        var ex = Assert.Throws<PatchValidationException>(() => service.Accept("p-does-not-exist"));
        Assert.Equal("patch_not_active", ex.Code);
    }

    [Fact]
    public void Accept_rejects_patch_that_is_not_awaiting_acceptance()
    {
        using var temp = CreateRepo(("ContextMessenger.slnx", "<Solution />"));
        var store = new InMemoryPatchSessionStore();
        var build = new QueueBuildRunner(BuildResult("failed"));
        var service = new PatchTransactionService(temp.Path, "TestRoot", store, build);

        var proposed = service.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "broken" }],
            Build = new PatchPolicy { Policy = "solution" },
        });
        Assert.Equal("needs_revision", proposed.PatchStatus);

        var ex = Assert.Throws<PatchValidationException>(() => service.Accept(proposed.PatchId!));
        Assert.Equal("invalid_patch_state", ex.Code);

        // The patch is untouched: still active, still needs_revision.
        Assert.Equal("needs_revision", service.Current().PatchStatus);
    }

    [Fact]
    public void Revert_from_awaiting_acceptance_restores_base_and_clears()
    {
        using var temp = CreateRepo(("replace.txt", "old"));
        var replacePath = Path.Combine(temp.Path, "replace.txt");
        var store = new InMemoryPatchSessionStore();
        var service = new PatchTransactionService(temp.Path, "TestRoot", store);

        var held = service.Propose(new ProposePatchRequest
        {
            DeferAcceptance = true,
            Files =
            [
                new PatchFileOperation
                {
                    Path = "replace.txt",
                    Operation = PatchFileOperationKind.Replace,
                    OldContentHash = ContentHash.ForFile(replacePath),
                    NewContent = "new",
                },
                new PatchFileOperation { Path = "create.txt", Operation = PatchFileOperationKind.Create, NewContent = "created" },
            ],
        });
        Assert.Equal("awaiting_acceptance", held.PatchStatus);

        var reverted = service.Revert(held.PatchId!);

        Assert.Equal("reverted", reverted.PatchStatus);
        Assert.Equal("old", File.ReadAllText(replacePath));
        Assert.False(File.Exists(Path.Combine(temp.Path, "create.txt")));
        Assert.Equal("none", service.Current().PatchStatus);
        Assert.Null(store.Load());
        Assert.True(new LibGit2SharpGitStatusService(temp.Path).GetStatus().IsClean);
    }

    [Fact]
    public void Propose_without_defer_acceptance_stages_and_closes_immediately()
    {
        // Golden master: the off-path is byte-for-byte today's behavior.
        using var temp = CreateRepo(("replace.txt", "old"));
        var replacePath = Path.Combine(temp.Path, "replace.txt");
        var store = new InMemoryPatchSessionStore();
        var service = new PatchTransactionService(temp.Path, "TestRoot", store);

        var result = service.Propose(new ProposePatchRequest
        {
            DeferAcceptance = false,
            Files =
            [
                new PatchFileOperation
                {
                    Path = "replace.txt",
                    Operation = PatchFileOperationKind.Replace,
                    OldContentHash = ContentHash.ForFile(replacePath),
                    NewContent = "new",
                },
            ],
        });

        Assert.Equal("accepted", result.PatchStatus);
        Assert.Equal("none", service.Current().PatchStatus);
        Assert.Null(store.Load());
        Assert.Contains(new LibGit2SharpGitStatusService(temp.Path).GetStatus().ChangedFiles,
            f => f is { Path: "replace.txt", Status: "staged_modified" });
    }

    [Fact]
    public void DeferAcceptanceByDefault_holds_passing_patch_without_request_flag()
    {
        using var temp = CreateRepo(("replace.txt", "old"));
        var replacePath = Path.Combine(temp.Path, "replace.txt");
        var store = new InMemoryPatchSessionStore();
        var service = new PatchTransactionService(
            temp.Path, "TestRoot", store, deferAcceptanceByDefault: true);

        // The request does NOT set DeferAcceptance; the per-root default drives it.
        var result = service.Propose(new ProposePatchRequest
        {
            Files =
            [
                new PatchFileOperation
                {
                    Path = "replace.txt",
                    Operation = PatchFileOperationKind.Replace,
                    OldContentHash = ContentHash.ForFile(replacePath),
                    NewContent = "new",
                },
            ],
        });

        Assert.Equal("awaiting_acceptance", result.PatchStatus);
        Assert.Equal("awaiting_acceptance", service.Current().PatchStatus);
        Assert.Contains(new LibGit2SharpGitStatusService(temp.Path).GetStatus().ChangedFiles,
            f => f is { Path: "replace.txt", Status: "modified_unstaged" });
    }

    [Fact]
    public void DeferAcceptanceByDefault_set_true_at_runtime_holds_passing_patch()
    {
        using var temp = CreateRepo(("a.txt", "old"));
        var path = Path.Combine(temp.Path, "a.txt");
        // Constructed with the default (false); flipped on at runtime.
        var service = new PatchTransactionService(temp.Path, "TestRoot")
        {
            DeferAcceptanceByDefault = true,
        };

        var result = service.Propose(new ProposePatchRequest
        {
            Files =
            [
                new PatchFileOperation
                {
                    Path = "a.txt",
                    Operation = PatchFileOperationKind.Replace,
                    OldContentHash = ContentHash.ForFile(path),
                    NewContent = "new",
                },
            ],
        });

        Assert.Equal("awaiting_acceptance", result.PatchStatus);
        Assert.Equal("awaiting_acceptance", service.Current().PatchStatus);
    }

    [Fact]
    public void Amend_with_defer_acceptance_holds_unstaged_and_keeps_transaction_open()
    {
        using var temp = CreateRepo(("ContextMessenger.slnx", "<Solution />"));
        var store = new InMemoryPatchSessionStore();
        var build = new QueueBuildRunner(BuildResult("failed"), BuildResult("ok"));
        var service = new PatchTransactionService(temp.Path, "TestRoot", store, build);

        var proposed = service.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new.txt", Operation = PatchFileOperationKind.Create, NewContent = "broken" }],
            Build = new PatchPolicy { Policy = "solution" },
        });
        Assert.Equal("needs_revision", proposed.PatchStatus);
        var filePath = Path.Combine(temp.Path, "new.txt");

        var amended = service.Amend(new AmendPatchRequest
        {
            PatchId = proposed.PatchId!,
            BaseRevision = proposed.Revision,
            DeferAcceptance = true,
            Files =
            [
                new PatchFileOperation
                {
                    Path = "new.txt",
                    Operation = PatchFileOperationKind.Replace,
                    OldContentHash = ContentHash.ForFile(filePath),
                    NewContent = "fixed",
                },
            ],
        });

        // Held at the new revision; applied but unstaged.
        Assert.Equal("awaiting_acceptance", amended.PatchStatus);
        Assert.Equal(2, amended.Revision);
        Assert.Equal("fixed", File.ReadAllText(filePath));
        Assert.Equal("awaiting_acceptance", service.Current().PatchStatus);
        Assert.Equal("awaiting_acceptance", store.Load()!.Status);
        Assert.Contains(new LibGit2SharpGitStatusService(temp.Path).GetStatus().ChangedFiles,
            f => f is { Path: "new.txt", Status: "untracked" });
    }

    [Fact]
    public void Recovery_preserves_file_operations_and_content_hashes_from_metadata()
    {
        using var temp = CreateRepo(("file.txt", "original"));
        var store = new InMemoryPatchSessionStore();
        var hash = HashText("original");
        var service = new PatchTransactionService(
            temp.Path, "TestRoot", store, buildRunner: new QueueBuildRunner(BuildResult("failed")));

        var proposed = service.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "file.txt", Operation = PatchFileOperationKind.Replace, OldContentHash = hash, NewContent = "changed" }],
            Build = new PatchPolicy { Policy = "solution" },
        });
        Assert.Equal("needs_revision", proposed.PatchStatus);

        // Simulate a fresh process: a new service over the same store and the still-dirty tree.
        var recovered = new PatchTransactionService(temp.Path, "TestRoot", store);
        var current = recovered.Current();

        Assert.Equal("needs_revision", current.PatchStatus);
        var file = Assert.Single(current.Files, f => f.Path == "file.txt");
        Assert.Equal("replace", file.Operation);
        // The optimistic-concurrency anchor survives recovery instead of being lost to null.
        Assert.Equal(hash, file.OldContentHash);
    }

    [Fact]
    public void Propose_defers_to_a_patch_started_on_another_root_after_construction()
    {
        using var rootA = CreateRepo(("a.txt", "a"));
        using var rootB = CreateRepo(("b.txt", "b"));
        var store = new InMemoryPatchSessionStore();
        // Both services are built while the shared store is empty, so neither sees a foreign patch yet.
        var serviceA = new PatchTransactionService(rootA.Path, "RootA", store, deferAcceptanceByDefault: true);
        var serviceB = new PatchTransactionService(rootB.Path, "RootB", store);

        var held = serviceA.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new-a.txt", Operation = PatchFileOperationKind.Create, NewContent = "x" }],
        });
        Assert.Equal("awaiting_acceptance", held.PatchStatus);

        var ex = Assert.Throws<PatchValidationException>(() => serviceB.Propose(new ProposePatchRequest
        {
            Files = [new PatchFileOperation { Path = "new-b.txt", Operation = PatchFileOperationKind.Create, NewContent = "y" }],
        }));
        Assert.Equal("patch_in_progress", ex.Code);

        // RootA's metadata is intact, not overwritten by RootB.
        Assert.Equal("RootA", store.Load()!.RootName);
    }

    [Fact]
    public void Propose_leaves_no_temp_files_behind()
    {
        using var temp = CreateRepo(("ContextMessenger.slnx", "<Solution />"), ("existing.txt", "old"));
        var service = new PatchTransactionService(temp.Path, "TestRoot");

        service.Propose(new ProposePatchRequest
        {
            Files =
            [
                new PatchFileOperation { Path = "nested/created.txt", Operation = PatchFileOperationKind.Create, NewContent = "new" },
                new PatchFileOperation { Path = "existing.txt", Operation = PatchFileOperationKind.Replace, OldContentHash = HashText("old"), NewContent = "updated" },
            ],
        });

        Assert.Equal("new", File.ReadAllText(Path.Combine(temp.Path, "nested", "created.txt")));
        Assert.Equal("updated", File.ReadAllText(Path.Combine(temp.Path, "existing.txt")));
        Assert.Empty(Directory.EnumerateFiles(temp.Path, "*.cmtmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void Git_status_excludes_patch_control_directory()
    {
        using var temp = CreateRepo(("file.txt", "tracked"));
        // Build/test output funnels into the control directory; a real build leaves it untracked.
        temp.CreateFile(".contextmessenger/patch-build/cache.bin", "junk");
        temp.CreateFile("real-untracked.txt", "x");

        var status = new LibGit2SharpGitStatusService(temp.Path).GetStatus();

        Assert.DoesNotContain(status.ChangedFiles, f => f.Path.StartsWith(".contextmessenger"));
        Assert.Contains(status.ChangedFiles, f => f.Path == "real-untracked.txt");
    }

    [Fact]
    public void Git_status_is_clean_when_only_control_directory_is_dirty()
    {
        using var temp = CreateRepo(("file.txt", "tracked"));
        temp.CreateFile(".contextmessenger/patch-test/results/run.trx", "<x/>");

        Assert.True(new LibGit2SharpGitStatusService(temp.Path).GetStatus().IsClean);
    }

    private static TempDirectory CreateRepo(params (string Path, string Content)[] files)
    {
        var temp = new TempDirectory();
        Repository.Init(temp.Path);
        foreach (var file in files)
            temp.CreateFile(file.Path, file.Content);

        using var repo = new Repository(temp.Path);
        Commands.Stage(repo, "*");
        repo.Commit("initial", Signature(), Signature());
        return temp;
    }

    public static IEnumerable<object[]> ReplaceLinesCases()
    {
        yield return ["one\ntwo\nthree\n", 2, 2, "two\n", "TWO\n", "one\nTWO\nthree\n"];
        yield return ["one\ntwo\n", 2, 2, "two\n", "TWO\n", "one\nTWO\n"];
        yield return ["one\ntwo", 2, 2, "two", "TWO", "one\nTWO"];
        yield return ["one\ntwo\n", 1, 2, "one\ntwo\n", "all\n", "all\n"];
        yield return ["one\ntwo\n", 1, 1, "one\n", "ONE\n", "ONE\ntwo\n"];
        yield return ["one\ntwo\nthree\n", 2, 2, "two\n", "TWO", "one\nTWOthree\n"];
    }

    private static string HashText(string text) =>
        ContentHash.ForBytes(System.Text.Encoding.UTF8.GetBytes(text));

    private static Signature Signature() =>
        new("ContextMessenger Tests", "tests@example.invalid", DateTimeOffset.UtcNow);

    private static PatchSessionMetadata Metadata(string rootPath, string? lastFailureStage = null)
    {
        using var repo = new Repository(rootPath);
        return new PatchSessionMetadata
        {
            PatchId = "p-recovered",
            RootName = "TestRoot",
            Status = "needs_revision",
            Revision = 2,
            Title = "Recovered",
            Description = "Recovered patch",
            CommitMessage = "Recovered commit message",
            CreatedAtUtc = DateTime.UnixEpoch,
            UpdatedAtUtc = DateTime.UnixEpoch.AddMinutes(1),
            BaseHeadSha = repo.Head.Tip!.Sha,
            LastFailureStage = lastFailureStage,
            BuildPolicy = new PatchPolicy { Policy = "none" },
            TestPolicy = new PatchPolicy { Policy = "none" },
        };
    }

    private sealed class InMemoryPatchSessionStore : IPatchSessionStore
    {
        private PatchSessionMetadata? _metadata;

        public InMemoryPatchSessionStore(PatchSessionMetadata? metadata = null)
        {
            _metadata = metadata;
        }

        public PatchSessionMetadata? Load() => _metadata;

        public void Save(PatchSessionMetadata metadata) => _metadata = metadata;

        public void Clear() => _metadata = null;
    }

    private sealed class FakeBuildRunner : IBuildRunner
    {
        public BuildResult Result { get; set; } = BuildResult("ok");

        public BuildRequest? LastRequest { get; private set; }

        public int Runs { get; private set; }

        public BuildResult Run(BuildRequest request)
        {
            LastRequest = request;
            Runs++;
            return Result;
        }
    }

    private sealed class QueueBuildRunner : IBuildRunner
    {
        private readonly Queue<BuildResult> _results;

        public QueueBuildRunner(params BuildResult[] results)
        {
            _results = new Queue<BuildResult>(results);
        }

        public BuildRequest? LastRequest { get; private set; }

        public BuildResult Run(BuildRequest request)
        {
            LastRequest = request;
            return _results.Count == 0 ? BuildResult("ok") : _results.Dequeue();
        }
    }

    private sealed class FakeTestRunner : ITestRunner
    {
        public TestResult Result { get; set; } = TestResult("ok");

        public TestRequest? LastRequest { get; private set; }

        public int Runs { get; private set; }

        public TestResult Run(TestRequest request)
        {
            LastRequest = request;
            Runs++;
            return Result;
        }
    }

    private sealed class QueueTestRunner : ITestRunner
    {
        private readonly Queue<TestResult> _results;

        public QueueTestRunner(params TestResult[] results)
        {
            _results = new Queue<TestResult>(results);
        }

        public TestRequest? LastRequest { get; private set; }

        public TestResult Run(TestRequest request)
        {
            LastRequest = request;
            return _results.Count == 0 ? TestResult("ok") : _results.Dequeue();
        }
    }

    private sealed class QueuePatchDiffVerifier : IPatchDiffVerifier
    {
        private readonly Queue<Exception?> _exceptions;

        public QueuePatchDiffVerifier(params Exception?[] exceptions)
        {
            _exceptions = new Queue<Exception?>(exceptions);
        }

        public int Count { get; private set; }

        public IReadOnlyList<PatchFileOperation>? LastOperations { get; private set; }

        public IReadOnlyList<GitStatusFile>? LastChangedFiles { get; private set; }

        public void Verify(IReadOnlyList<PatchFileOperation> operations, IReadOnlyList<GitStatusFile> changedFiles)
        {
            Count++;
            LastOperations = operations;
            LastChangedFiles = changedFiles;

            if (_exceptions.Count == 0)
                return;

            var exception = _exceptions.Dequeue();
            if (exception is not null)
                throw exception;
        }
    }

    private sealed class FakeRoslynNavigationService : IRoslynNavigationService
    {
        private readonly GetSymbolSourceResult? _result;
        private readonly Exception? _exception;

        public FakeRoslynNavigationService(GetSymbolSourceResult result)
        {
            _result = result;
        }

        public FakeRoslynNavigationService(Exception exception)
        {
            _exception = exception;
        }

        public int InvalidateCount { get; private set; }

        public string GetWorkspaceVersion() => "test";

        public DocumentSymbolsResult GetDocumentSymbols(DocumentSymbolsQuery query) => throw new NotSupportedException();

        public FindSymbolsResult FindSymbols(FindSymbolQuery query) => throw new NotSupportedException();

        public FindReferencesResult FindReferences(FindReferencesQuery query) => throw new NotSupportedException();

        public GotoDefinitionResult GotoDefinition(GotoDefinitionQuery query) => throw new NotSupportedException();

        public FindImplementationsResult FindImplementations(FindImplementationsQuery query) => throw new NotSupportedException();

        public FindCallersResult FindCallers(FindCallersQuery query) => throw new NotSupportedException();

        public FindDerivedTypesResult FindDerivedTypes(FindDerivedTypesQuery query) => throw new NotSupportedException();

        public FindOverridesResult FindOverrides(FindOverridesQuery query) => throw new NotSupportedException();

        public SymbolInfoResult GetSymbolInfo(GetSymbolInfoQuery query) => throw new NotSupportedException();

        public GetSymbolSourceResult GetSymbolSource(GetSymbolSourceQuery query)
        {
            if (_exception is not null)
                throw _exception;

            return _result!;
        }

        public void InvalidateWorkspace() => InvalidateCount++;
    }

    private sealed class FakeWorkspaceInvalidator : ContextMessenger.Core.Roslyn.IRoslynWorkspaceInvalidator
    {
        public int Count { get; private set; }

        public void InvalidateWorkspace() => Count++;
    }

    private static BuildResult BuildResult(string status, IReadOnlyList<BuildDiagnostic>? diagnostics = null) => new()
    {
        Status = status,
        Path = "ContextMessenger.slnx",
        Configuration = "Debug",
        DurationMs = 10,
        ExitCode = status == "ok" ? 0 : 1,
        Diagnostics = diagnostics ?? [],
    };

    private static TestResult TestResult(string status, IReadOnlyList<BuildDiagnostic>? diagnostics = null) => new()
    {
        Status = status,
        Path = "ContextMessenger.slnx",
        Configuration = "Debug",
        DurationMs = 10,
        ExitCode = status == "ok" ? 0 : 1,
        TotalTests = status == "ok" ? 3 : 2,
        ExecutedTests = status == "ok" ? 3 : 2,
        PassedTests = status == "ok" ? 3 : 1,
        FailedTests = status == "ok" ? 0 : 1,
        SkippedTests = 0,
        Diagnostics = diagnostics ?? [],
    };
}
