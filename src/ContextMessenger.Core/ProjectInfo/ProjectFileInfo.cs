namespace ContextMessenger.Core.ProjectInfo;

using System.Text.Json.Serialization;

public sealed record ProjectFileInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("path")]
    public string Path { get; init; } = "";

    [JsonPropertyName("targetFramework")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetFramework { get; init; }

    [JsonPropertyName("targetFrameworks")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? TargetFrameworks { get; init; }

    [JsonPropertyName("outputType")]
    public string OutputType { get; init; } = "Library";

    [JsonPropertyName("nullable")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Nullable { get; init; }

    [JsonPropertyName("langVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LangVersion { get; init; }

    [JsonPropertyName("isTestProject")]
    public bool IsTestProject { get; init; }

    [JsonPropertyName("projectReferences")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? ProjectReferences { get; init; }

    [JsonPropertyName("packages")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<PackageReferenceInfo>? Packages { get; init; }
}
