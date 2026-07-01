namespace ContextMessenger.Roslyn.Tests;

using ContextMessenger.Core.Roslyn;

public sealed class DocumentSymbolServiceTests
{
    [Fact]
    public void GetDocumentSymbols_returns_types_and_member_children()
    {
        using var temp = new TempDirectory();
        temp.CreateFile(
            "src/Sample.cs",
            """
            namespace Demo;

            public sealed class Widget
            {
                private readonly string _name;

                public Widget(string name)
                {
                    _name = name;
                }

                public string Name { get; }

                public void Run(int count)
                {
                }

                private void Hidden()
                {
                }

                public sealed class Nested
                {
                }
            }
            """);
        var service = new DocumentSymbolService(temp.Path);

        var result = service.GetDocumentSymbols(new() { RelativePath = "src/Sample.cs" });

        Assert.Equal("src/Sample.cs", result.Path);
        var widget = Assert.Single(result.Symbols);
        Assert.Equal("Widget", widget.Name);
        Assert.Equal("class", widget.Kind);
        Assert.Equal("public sealed class Widget", widget.Signature);
        Assert.Contains(widget.Children, symbol => symbol is { Name: "_name", Kind: "field" });
        Assert.Contains(widget.Children, symbol => symbol is { Name: "Widget", Kind: "constructor" });
        Assert.Contains(widget.Children, symbol => symbol is { Name: "Widget", Kind: "constructor", Signature: "public Widget(string name)" });
        Assert.Contains(widget.Children, symbol => symbol is { Name: "Name", Kind: "property" });
        Assert.Contains(widget.Children, symbol => symbol is { Name: "Run", Kind: "method" });
        Assert.Contains(widget.Children, symbol => symbol is { Name: "Hidden", Kind: "method" });
        Assert.Contains(widget.Children, symbol => symbol is { Name: "Nested", Kind: "class" });
    }

    [Fact]
    public void GetDocumentSymbols_can_filter_non_public_members()
    {
        using var temp = new TempDirectory();
        temp.CreateFile(
            "Sample.cs",
            """
            public class PublicType
            {
                public void Visible() { }
                private void Hidden() { }
            }

            internal class InternalType
            {
            }
            """);
        var service = new DocumentSymbolService(temp.Path);

        var result = service.GetDocumentSymbols(new()
        {
            RelativePath = "Sample.cs",
            IncludeNonPublic = false,
        });

        var type = Assert.Single(result.Symbols);
        Assert.Equal("PublicType", type.Name);
        Assert.Single(type.Children);
        Assert.Equal("Visible", type.Children[0].Name);
    }

    [Fact]
    public void GetDocumentSymbols_returns_enum_members()
    {
        using var temp = new TempDirectory();
        temp.CreateFile(
            "Kind.cs",
            """
            public enum Kind
            {
                One,
                Two
            }
            """);
        var service = new DocumentSymbolService(temp.Path);

        var result = service.GetDocumentSymbols(new() { RelativePath = "Kind.cs" });

        var kind = Assert.Single(result.Symbols);
        Assert.Equal("enum", kind.Kind);
        Assert.Equal(["One", "Two"], kind.Children.Select(symbol => symbol.Name).ToArray());
    }

    [Fact]
    public void GetDocumentSymbols_rejects_non_csharp_files()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("docs/readme.md", "# Title");
        var service = new DocumentSymbolService(temp.Path);

        var ex = Assert.Throws<NotSupportedException>(() =>
            service.GetDocumentSymbols(new() { RelativePath = "docs/readme.md" }));

        Assert.Contains(".cs", ex.Message);
    }

    [Fact]
    public void Semantic_symbol_signatures_are_short_csharp_declarations()
    {
        using var temp = new TempDirectory();
        CreateSingleProjectSolution(temp);
        temp.CreateFile(
            "src/Demo/Parser.cs",
            """
            namespace Demo;

            public static class Parser
            {
                public static IReadOnlyList<string> ParseBody(string input)
                {
                    return [];
                }

                public static IReadOnlyList<string> ParseBodyAndValidate(string input)
                {
                    return ParseBody(input);
                }
            }
            """);
        var service = new DocumentSymbolService(temp.Path);

        var parser = Assert.Single(service.FindSymbols(new()
        {
            Name = "Parser",
            Kinds = ["class"],
        }).Matches);
        var parseBody = Assert.Single(service.GotoDefinition(new()
        {
            RelativePath = "src/Demo/Parser.cs",
            Line = 12,
            Column = 25,
        }).Definitions);

        Assert.Equal("public static class Parser", parser.Signature);
        Assert.Equal("public static IReadOnlyList<string> ParseBody(string input)", parseBody.Signature);
        Assert.DoesNotContain("Demo.Parser", parser.Signature);
        Assert.DoesNotContain("System.Collections.Generic", parseBody.Signature);
    }

    [Fact]
    public void GetSymbolSource_uses_roslyn_declaration_span_when_body_contains_brace_literal()
    {
        using var temp = new TempDirectory();
        CreateSingleProjectSolution(temp);
        temp.CreateFile(
            "src/Demo/Parser.cs",
            """
            namespace Demo;

            public static class Parser
            {
                public static string ParseBody(string input)
                {
                    return input[0] switch
                    {
                        '{' => ParseObject(input),
                        '[' => ParseArray(input),
                        _ => input,
                    };
                }

                public static string ParseBodyAndValidate(string input)
                {
                    return ParseBody(input);
                }

                private static string ParseObject(string input) => input;

                private static string ParseArray(string input) => input;
            }
            """);
        var service = new DocumentSymbolService(temp.Path);
        var symbol = Assert.Single(service.FindSymbols(new()
        {
            Name = "ParseBody",
            Kinds = ["method"],
            IncludeNonPublic = true,
        }).Matches);

        var result = service.GetSymbolSource(new()
        {
            SymbolId = symbol.SymbolId,
        });

        Assert.Equal("ParseBody", result.Symbol!.Name);
        Assert.Equal("src/Demo/Parser.cs", result.Source!.Path);
        Assert.Contains("'{' => ParseObject(input)", result.Source.Text);
        Assert.Contains("return input[0] switch", result.Source.Text);
        Assert.DoesNotContain("ParseBodyAndValidate", result.Source.Text);
    }

    [Fact]
    public void GetSymbolSource_by_location_resolves_declaration_identifier()
    {
        using var temp = new TempDirectory();
        CreateSingleProjectSolution(temp);
        temp.CreateFile(
            "src/Demo/Parser.cs",
            """
            namespace Demo;

            public static class Parser
            {
                public static string Value()
                {
                    return "ok";
                }

                public static string Other() => "other";
            }
            """);
        var service = new DocumentSymbolService(temp.Path);

        var result = service.GetSymbolSource(new()
        {
            RelativePath = "src/Demo/Parser.cs",
            Line = 5,
            Column = 26,
        });

        Assert.Equal("Value", result.Symbol!.Name);
        Assert.Contains("public static string Value()", result.Source!.Text);
        Assert.DoesNotContain("public static string Other", result.Source.Text);
    }

    [Fact]
    public void GetSymbolSource_rejects_declarations_that_exceed_max_lines()
    {
        using var temp = new TempDirectory();
        CreateSingleProjectSolution(temp);
        temp.CreateFile(
            "src/Demo/Parser.cs",
            """
            namespace Demo;

            public static class Parser
            {
                public static string Value()
                {
                    return "ok";
                }
            }
            """);
        var service = new DocumentSymbolService(temp.Path);
        var symbol = Assert.Single(service.FindSymbols(new()
        {
            Name = "Value",
            Kinds = ["method"],
        }).Matches);

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.GetSymbolSource(new()
            {
                SymbolId = symbol.SymbolId,
                MaxLines = 1,
            }));

        Assert.Contains("maxLines", ex.Message);
    }

    [Fact]
    public void GetSymbolSource_includes_documentation_and_attributes()
    {
        using var temp = new TempDirectory();
        CreateSingleProjectSolution(temp);
        temp.CreateFile(
            "src/Demo/Parser.cs",
            """
            namespace Demo;

            public static class Parser
            {
                /// <summary>
                /// Parses one value.
                /// </summary>
                [System.Obsolete("Use ParseNew")]
                public static string ParseOld(string input)
                {
                    return input;
                }
            }
            """);
        var service = new DocumentSymbolService(temp.Path);
        var symbol = Assert.Single(service.FindSymbols(new()
        {
            Name = "ParseOld",
            Kinds = ["method"],
        }).Matches);

        var result = service.GetSymbolSource(new()
        {
            SymbolId = symbol.SymbolId,
        });

        Assert.Contains("/// <summary>", result.Source!.Text);
        Assert.Contains("[System.Obsolete(\"Use ParseNew\")]", result.Source.Text);
        Assert.Contains("public static string ParseOld(string input)", result.Source.Text);
    }

    [Fact]
    public void GetSymbolSource_handles_expression_bodied_members()
    {
        using var temp = new TempDirectory();
        CreateSingleProjectSolution(temp);
        temp.CreateFile(
            "src/Demo/Parser.cs",
            """
            namespace Demo;

            public static class Parser
            {
                public static string Parse(string input) => input.Trim();

                public static string Other() => "other";
            }
            """);
        var service = new DocumentSymbolService(temp.Path);
        var symbol = Assert.Single(service.FindSymbols(new()
        {
            Name = "Parse",
            Kinds = ["method"],
        }).Matches);

        var result = service.GetSymbolSource(new()
        {
            SymbolId = symbol.SymbolId,
        });

        Assert.Contains("public static string Parse(string input) => input.Trim();", result.Source!.Text);
        Assert.DoesNotContain("public static string Other", result.Source.Text);
    }

    [Fact]
    public void GetSymbolSource_rejects_symbol_id_and_location_together()
    {
        using var temp = new TempDirectory();
        CreateSingleProjectSolution(temp);
        temp.CreateFile(
            "src/Demo/Parser.cs",
            """
            namespace Demo;

            public static class Parser
            {
                public static string Parse(string input) => input;
            }
            """);
        var service = new DocumentSymbolService(temp.Path);

        var ex = Assert.Throws<ArgumentException>(() =>
            service.GetSymbolSource(new()
            {
                SymbolId = "M:Demo.Parser.Parse(System.String)",
                RelativePath = "src/Demo/Parser.cs",
                Line = 5,
                Column = 33,
            }));

        Assert.Contains("exactly one selector", ex.Message);
    }

    [Fact]
    public void GetSymbolSource_can_resolve_by_unique_name()
    {
        using var temp = new TempDirectory();
        CreateSingleProjectSolution(temp);
        temp.CreateFile(
            "src/Demo/Parser.cs",
            """
            namespace Demo;

            public static class Parser
            {
                public static string Parse(string input) => input;
            }
            """);
        var service = new DocumentSymbolService(temp.Path);

        var result = service.GetSymbolSource(new()
        {
            Name = "Parse",
            Kinds = ["method"],
        });

        Assert.Equal("Parse", result.Symbol!.Name);
        Assert.Contains("public static string Parse(string input) => input;", result.Source!.Text);
    }

    [Fact]
    public void GetSymbolSource_rejects_name_when_multiple_symbols_match()
    {
        using var temp = new TempDirectory();
        CreateSingleProjectSolution(temp);
        temp.CreateFile(
            "src/Demo/Parser.cs",
            """
            namespace Demo;

            public static class Parser
            {
                public static string Parse(string input) => input;
            }

            public static class OtherParser
            {
                public static string Parse(string input) => input;
            }
            """);
        var service = new DocumentSymbolService(temp.Path);

        var ex = Assert.Throws<ArgumentException>(() =>
            service.GetSymbolSource(new()
            {
                Name = "Parse",
                Kinds = ["method"],
            }));

        Assert.Contains("matched multiple symbols", ex.Message);
        Assert.Contains("symbolId", ex.Message);
    }

    [Fact]
    public void GetSymbolSource_returns_full_field_declaration()
    {
        using var temp = new TempDirectory();
        CreateSingleProjectSolution(temp);
        temp.CreateFile(
            "src/Demo/Parser.cs",
            """
            namespace Demo;

            public sealed class Parser
            {
                [System.Obsolete("Use Name")]
                private readonly string _name = "demo";

                public string Name => _name;
            }
            """);
        var service = new DocumentSymbolService(temp.Path);
        var symbol = Assert.Single(service.FindSymbols(new()
        {
            Name = "_name",
            Kinds = ["field"],
            IncludeNonPublic = true,
        }).Matches);

        var result = service.GetSymbolSource(new()
        {
            SymbolId = symbol.SymbolId,
        });

        Assert.Contains("[System.Obsolete(\"Use Name\")]", result.Source!.Text);
        Assert.Contains("private readonly string _name = \"demo\";", result.Source.Text);
        Assert.DoesNotContain("public string Name", result.Source.Text);
    }

    [Fact]
    public void GetSymbolSource_returns_full_event_field_declaration()
    {
        using var temp = new TempDirectory();
        CreateSingleProjectSolution(temp);
        temp.CreateFile(
            "src/Demo/Parser.cs",
            """
            namespace Demo;

            public sealed class Parser
            {
                public event System.EventHandler? Changed;

                public void Raise() => Changed?.Invoke(this, System.EventArgs.Empty);
            }
            """);
        var service = new DocumentSymbolService(temp.Path);
        var symbol = Assert.Single(service.FindSymbols(new()
        {
            Name = "Changed",
            Kinds = ["event"],
        }).Matches);

        var result = service.GetSymbolSource(new()
        {
            SymbolId = symbol.SymbolId,
        });

        Assert.Contains("public event System.EventHandler? Changed;", result.Source!.Text);
        Assert.DoesNotContain("public void Raise", result.Source.Text);
    }

    [Fact]
    public void GetSymbolSource_returns_constructor_declaration()
    {
        using var temp = new TempDirectory();
        CreateSingleProjectSolution(temp);
        temp.CreateFile(
            "src/Demo/Parser.cs",
            """
            namespace Demo;

            public sealed class Parser
            {
                private readonly string _name;

                public Parser(string name)
                {
                    _name = name;
                }

                public string Name => _name;
            }
            """);
        var service = new DocumentSymbolService(temp.Path);
        var result = service.GetSymbolSource(new()
        {
            RelativePath = "src/Demo/Parser.cs",
            Line = 7,
            Column = 16,
        });

        Assert.Contains("public Parser(string name)", result.Source!.Text);
        Assert.Contains("_name = name;", result.Source.Text);
        Assert.DoesNotContain("public string Name", result.Source.Text);
    }

    [Fact]
    public void GetSymbolSource_returns_type_declaration()
    {
        using var temp = new TempDirectory();
        CreateSingleProjectSolution(temp);
        temp.CreateFile(
            "src/Demo/Parser.cs",
            """
            namespace Demo;

            /// <summary>
            /// Parser type.
            /// </summary>
            public sealed class Parser
            {
                public string Name => "Parser";
            }

            public sealed class Other
            {
            }
            """);
        var service = new DocumentSymbolService(temp.Path);
        var symbol = Assert.Single(service.FindSymbols(new()
        {
            Name = "Parser",
            Kinds = ["class"],
        }).Matches);

        var result = service.GetSymbolSource(new()
        {
            SymbolId = symbol.SymbolId,
        });

        Assert.Contains("/// <summary>", result.Source!.Text);
        Assert.Contains("public sealed class Parser", result.Source.Text);
        Assert.Contains("public string Name => \"Parser\";", result.Source.Text);
        Assert.DoesNotContain("public sealed class Other", result.Source.Text);
    }

    [Fact]
    public void GetSymbolSource_for_parameter_location_returns_containing_method_declaration()
    {
        using var temp = new TempDirectory();
        CreateSingleProjectSolution(temp);
        temp.CreateFile(
            "src/Demo/Parser.cs",
            """
            namespace Demo;

            public sealed class Parser
            {
                public string Parse(string input)
                {
                    return input.Trim();
                }

                public string Other() => "other";
            }
            """);
        var service = new DocumentSymbolService(temp.Path);

        var result = service.GetSymbolSource(new()
        {
            RelativePath = "src/Demo/Parser.cs",
            Line = 7,
            Column = 16,
        });

        Assert.Equal("input", result.Symbol!.Name);
        Assert.Contains("public string Parse(string input)", result.Source!.Text);
        Assert.Contains("return input.Trim();", result.Source.Text);
        Assert.DoesNotContain("public string Other", result.Source.Text);
    }

    [Fact]
    public void GetSymbolSource_for_property_accessor_location_returns_property_declaration()
    {
        using var temp = new TempDirectory();
        CreateSingleProjectSolution(temp);
        temp.CreateFile(
            "src/Demo/Parser.cs",
            """
            namespace Demo;

            public sealed class Parser
            {
                public string Name
                {
                    get
                    {
                        return "Parser";
                    }
                }

                public string Other => "other";
            }
            """);
        var service = new DocumentSymbolService(temp.Path);

        var result = service.GetSymbolSource(new()
        {
            RelativePath = "src/Demo/Parser.cs",
            Line = 7,
            Column = 13,
        });

        Assert.Contains("public string Name", result.Source!.Text);
        Assert.Contains("return \"Parser\";", result.Source.Text);
        Assert.DoesNotContain("public string Other", result.Source.Text);
    }

    [Fact]
    public void GetSymbolSource_for_type_parameter_location_returns_containing_type_declaration()
    {
        using var temp = new TempDirectory();
        CreateSingleProjectSolution(temp);
        temp.CreateFile(
            "src/Demo/Box.cs",
            """
            namespace Demo;

            public sealed class Box<T>
            {
                public T Value { get; }

                public Box(T value)
                {
                    Value = value;
                }
            }

            public sealed class Other
            {
            }
            """);
        var service = new DocumentSymbolService(temp.Path);

        var result = service.GetSymbolSource(new()
        {
            RelativePath = "src/Demo/Box.cs",
            Line = 5,
            Column = 12,
        });

        Assert.Equal("T", result.Symbol!.Name);
        Assert.Contains("public sealed class Box<T>", result.Source!.Text);
        Assert.Contains("public T Value { get; }", result.Source.Text);
        Assert.DoesNotContain("public sealed class Other", result.Source.Text);
    }

    [Fact]
    public void GetSymbolSource_for_local_function_parameter_location_returns_local_function_declaration()
    {
        using var temp = new TempDirectory();
        CreateSingleProjectSolution(temp);
        temp.CreateFile(
            "src/Demo/Parser.cs",
            """
            namespace Demo;

            public sealed class Parser
            {
                public string Parse(string input)
                {
                    return Normalize(input);

                    static string Normalize(string value)
                    {
                        return value.Trim();
                    }
                }
            }
            """);
        var service = new DocumentSymbolService(temp.Path);

        var result = service.GetSymbolSource(new()
        {
            RelativePath = "src/Demo/Parser.cs",
            Line = 9,
            Column = 42,
        });

        Assert.Equal("value", result.Symbol!.Name);
        Assert.Contains("static string Normalize(string value)", result.Source!.Text);
        Assert.Contains("return value.Trim();", result.Source.Text);
        Assert.DoesNotContain("return Normalize(input);", result.Source.Text);
    }

    [Fact]
    public void GetSymbolSource_returns_enum_member_declaration()
    {
        using var temp = new TempDirectory();
        CreateSingleProjectSolution(temp);
        temp.CreateFile(
            "src/Demo/ParserKind.cs",
            """
            namespace Demo;

            public enum ParserKind
            {
                Unknown,
                [System.Obsolete("Use Json")]
                LegacyJson = 1,
                Json = 2,
            }
            """);
        var service = new DocumentSymbolService(temp.Path);
        var symbol = Assert.Single(service.FindSymbols(new()
        {
            Name = "LegacyJson",
            Kinds = ["field"],
        }).Matches);

        var result = service.GetSymbolSource(new()
        {
            SymbolId = symbol.SymbolId,
        });

        Assert.Contains("[System.Obsolete(\"Use Json\")]", result.Source!.Text);
        Assert.Contains("LegacyJson = 1", result.Source.Text);
        Assert.DoesNotContain("Json = 2", result.Source.Text);
    }

    [Fact]
    public void GetSymbolSource_for_delegate_parameter_location_returns_delegate_declaration()
    {
        using var temp = new TempDirectory();
        CreateSingleProjectSolution(temp);
        temp.CreateFile(
            "src/Demo/ParserDelegate.cs",
            """
            namespace Demo;

            public delegate string ParserDelegate(string input);

            public sealed class Parser
            {
            }
            """);
        var service = new DocumentSymbolService(temp.Path);

        var result = service.GetSymbolSource(new()
        {
            RelativePath = "src/Demo/ParserDelegate.cs",
            Line = 3,
            Column = 46,
        });

        Assert.Equal("input", result.Symbol!.Name);
        Assert.Contains("public delegate string ParserDelegate(string input);", result.Source!.Text);
        Assert.DoesNotContain("public sealed class Parser", result.Source.Text);
    }

    [Fact]
    public void GetSymbolSource_name_selector_respects_project_filter()
    {
        using var temp = new TempDirectory();
        var projectOneGuid = Guid.NewGuid().ToString("B").ToUpperInvariant();
        var projectTwoGuid = Guid.NewGuid().ToString("B").ToUpperInvariant();
        temp.CreateFile(
            "ContextMessenger.Roslyn.Tests.Sample.sln",
            $$"""
              
              Microsoft Visual Studio Solution File, Format Version 12.00
              # Visual Studio Version 17
              VisualStudioVersion = 17.0.31903.59
              MinimumVisualStudioVersion = 10.0.40219.1
              Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "DemoOne", "src\DemoOne\DemoOne.csproj", "{{projectOneGuid}}"
              EndProject
              Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "DemoTwo", "src\DemoTwo\DemoTwo.csproj", "{{projectTwoGuid}}"
              EndProject
              Global
              	GlobalSection(SolutionConfigurationPlatforms) = preSolution
              		Debug|Any CPU = Debug|Any CPU
              	EndGlobalSection
              	GlobalSection(ProjectConfigurationPlatforms) = postSolution
              		{{projectOneGuid}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
              		{{projectOneGuid}}.Debug|Any CPU.Build.0 = Debug|Any CPU
              		{{projectTwoGuid}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
              		{{projectTwoGuid}}.Debug|Any CPU.Build.0 = Debug|Any CPU
              	EndGlobalSection
              EndGlobal
              """);
        temp.CreateFile(
            "src/DemoOne/DemoOne.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        temp.CreateFile(
            "src/DemoTwo/DemoTwo.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        temp.CreateFile(
            "src/DemoOne/Parser.cs",
            """
            namespace DemoOne;

            public sealed class Parser
            {
                public string Value() => "one";
            }
            """);
        temp.CreateFile(
            "src/DemoTwo/Parser.cs",
            """
            namespace DemoTwo;

            public sealed class Parser
            {
                public string Value() => "two";
            }
            """);
        var service = new DocumentSymbolService(temp.Path);

        var result = service.GetSymbolSource(new()
        {
            Name = "Parser",
            Kinds = ["class"],
            Project = "DemoTwo",
        });

        Assert.Equal("DemoTwo", result.Symbol!.ProjectName);
        Assert.Equal("src/DemoTwo/Parser.cs", result.Source!.Path);
        Assert.Contains("public string Value() => \"two\";", result.Source.Text);
        Assert.DoesNotContain("namespace DemoOne;", result.Source.Text);
    }

    [Fact]
    public void InvalidateWorkspace_changes_workspace_version()
    {
        using var temp = new TempDirectory();
        CreateSingleProjectSolution(temp);
        temp.CreateFile(
            "src/Demo/Parser.cs",
            """
            namespace Demo;

            public static class Parser
            {
            }
            """);
        var service = new DocumentSymbolService(temp.Path);
        var before = service.GetWorkspaceVersion();

        service.InvalidateWorkspace();

        var after = service.GetWorkspaceVersion();
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void InvalidateWorkspace_reloads_source_snapshot_on_next_query()
    {
        using var temp = new TempDirectory();
        CreateSingleProjectSolution(temp);
        var parserPath = temp.CreateFile(
            "src/Demo/Parser.cs",
            """
            namespace Demo;

            public static class Parser
            {
            }
            """);
        var service = new DocumentSymbolService(temp.Path);
        var before = service.FindSymbols(new()
        {
            Name = "Parser",
            Kinds = ["class"],
        });

        File.WriteAllText(
            parserPath,
            """
            namespace Demo;

            public static class PatchedParser
            {
            }
            """);
        service.InvalidateWorkspace();
        var invalidatedVersion = service.GetWorkspaceVersion();

        var after = service.FindSymbols(new()
        {
            Name = "PatchedParser",
            Kinds = ["class"],
        });

        Assert.NotEqual(before.WorkspaceVersion, invalidatedVersion);
        Assert.Equal(invalidatedVersion, after.WorkspaceVersion);
        Assert.Contains(after.Matches, symbol => symbol.Name == "PatchedParser");
    }

    private static void CreateSingleProjectSolution(TempDirectory temp)
    {
        var projectGuid = Guid.NewGuid().ToString("B").ToUpperInvariant();
        temp.CreateFile(
            "ContextMessenger.Roslyn.Tests.Sample.sln",
            $$"""
              
              Microsoft Visual Studio Solution File, Format Version 12.00
              # Visual Studio Version 17
              VisualStudioVersion = 17.0.31903.59
              MinimumVisualStudioVersion = 10.0.40219.1
              Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Demo", "src\Demo\Demo.csproj", "{{projectGuid}}"
              EndProject
              Global
              	GlobalSection(SolutionConfigurationPlatforms) = preSolution
              		Debug|Any CPU = Debug|Any CPU
              	EndGlobalSection
              	GlobalSection(ProjectConfigurationPlatforms) = postSolution
              		{{projectGuid}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
              		{{projectGuid}}.Debug|Any CPU.Build.0 = Debug|Any CPU
              	EndGlobalSection
              EndGlobal
              """);
        temp.CreateFile(
            "src/Demo/Demo.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
    }
}
