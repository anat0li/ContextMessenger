using System.Text.Json.Serialization;

namespace ContextMessenger.Core.Roslyn;

public sealed record SymbolInfoResult
{
    [JsonPropertyName("workspaceVersion")]
    public string WorkspaceVersion { get; init; } = "";

    [JsonPropertyName("symbol")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SymbolSummary? Symbol { get; init; }

    [JsonPropertyName("documentationXml")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DocumentationXml { get; init; }

    [JsonPropertyName("attributes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IReadOnlyList<string> Attributes { get; init; } = [];

    [JsonPropertyName("baseTypes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IReadOnlyList<string> BaseTypes { get; init; } = [];

    [JsonPropertyName("implementedInterfaces")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IReadOnlyList<string> ImplementedInterfaces { get; init; } = [];

    [JsonPropertyName("typeParameters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IReadOnlyList<string> TypeParameters { get; init; } = [];

    [JsonPropertyName("genericConstraints")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IReadOnlyList<string> GenericConstraints { get; init; } = [];

    [JsonPropertyName("returnType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReturnType { get; init; }

    [JsonPropertyName("parameters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IReadOnlyList<SymbolParameterInfo> Parameters { get; init; } = [];

    [JsonPropertyName("isAsync")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsAsync { get; init; }

    [JsonPropertyName("isStatic")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsStatic { get; init; }

    [JsonPropertyName("isAbstract")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsAbstract { get; init; }

    [JsonPropertyName("isVirtual")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsVirtual { get; init; }

    [JsonPropertyName("isOverride")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsOverride { get; init; }

    [JsonPropertyName("overriddenMethod")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OverriddenMethod { get; init; }

    [JsonPropertyName("implementedInterfaceMembers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IReadOnlyList<string> ImplementedInterfaceMembers { get; init; } = [];
}

public sealed record SymbolParameterInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("type")]
    public string Type { get; init; } = "";

    [JsonPropertyName("refKind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RefKind { get; init; }

    [JsonPropertyName("isOptional")]
    public bool IsOptional { get; init; }

    [JsonPropertyName("defaultValue")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultValue { get; init; }
}
