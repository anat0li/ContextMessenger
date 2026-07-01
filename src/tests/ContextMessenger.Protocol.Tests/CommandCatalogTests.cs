using System.Reflection;
using System.Text.Json.Serialization;
using ContextMessenger.Core.Meta;
using ContextMessenger.Protocol.Commands;

namespace ContextMessenger.Protocol.Tests;

public sealed class CommandCatalogTests
{
    [Fact]
    public void Catalog_contains_every_CommandTypes_constant()
    {
        var constants = typeof(CommandTypes)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToArray();

        var catalogNames = CommandCatalog.GetAll().Select(c => c.Name).ToHashSet();

        foreach (var name in constants)
            Assert.Contains(name, catalogNames);
    }

    [Fact]
    public void Find_is_case_insensitive_and_returns_null_for_unknown()
    {
        Assert.NotNull(CommandCatalog.Find("FIND_SYMBOL"));
        Assert.Null(CommandCatalog.Find("not_a_real_command"));
    }

    [Fact]
    public void Set_root_is_tagged_with_session_side_effects()
    {
        var descriptor = CommandCatalog.Find(CommandTypes.SetRoot);
        Assert.NotNull(descriptor);
        Assert.Equal(CommandCatalog.SideEffectsSession, descriptor!.SideEffects);
    }

    [Fact]
    public void Read_only_commands_default_to_no_side_effects()
    {
        foreach (var name in new[]
        {
            CommandTypes.CurrentContext,
            CommandTypes.ListRoots,
            CommandTypes.ListTargets,
            CommandTypes.Capabilities,
            CommandTypes.Tree,
            CommandTypes.ReadFile,
            CommandTypes.SearchText,
            CommandTypes.ListFiles,
            CommandTypes.ProjectInfo,
            CommandTypes.DocumentSymbols,
            CommandTypes.FindSymbol,
            CommandTypes.FindReferences,
            CommandTypes.GotoDefinition,
            CommandTypes.FindImplementations,
            CommandTypes.FindCallers,
            CommandTypes.FindDerivedTypes,
            CommandTypes.FindOverrides,
            CommandTypes.GetSymbolInfo,
            CommandTypes.GetSymbolSource,
        })
        {
            var descriptor = CommandCatalog.Find(name);
            Assert.NotNull(descriptor);
            Assert.Equal(CommandCatalog.SideEffectsNone, descriptor!.SideEffects);
        }
    }

    [Fact]
    public void Capabilities_response_includes_sideEffects_for_every_command()
    {
        foreach (var descriptor in CommandCatalog.GetAll())
            Assert.False(string.IsNullOrEmpty(descriptor.SideEffects));
    }

    [Theory]
    [InlineData(CommandTypes.Tree, typeof(TreeCommandParams))]
    [InlineData(CommandTypes.ReadFile, typeof(ReadFileCommandParams))]
    [InlineData(CommandTypes.FindSymbol, typeof(FindSymbolCommandParams))]
    [InlineData(CommandTypes.GetSymbolSource, typeof(GetSymbolSourceCommandParams))]
    [InlineData(CommandTypes.SetRoot, typeof(SetRootCommandParams))]
    public void Descriptor_parameter_names_match_command_params_class(string commandName, Type paramsType)
    {
        var descriptor = CommandCatalog.Find(commandName);
        Assert.NotNull(descriptor);

        var descriptorParamNames = descriptor!.Parameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var classParamNames = paramsType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? ToCamelCase(p.Name))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(classParamNames, descriptorParamNames);
    }

    private static string ToCamelCase(string s) =>
        string.IsNullOrEmpty(s) || char.IsLower(s[0]) ? s : char.ToLowerInvariant(s[0]) + s[1..];
}
