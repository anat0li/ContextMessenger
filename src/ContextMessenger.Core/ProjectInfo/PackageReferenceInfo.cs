namespace ContextMessenger.Core.ProjectInfo;

using System.Text.Json.Serialization;

public sealed record PackageReferenceInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; init; }
}
