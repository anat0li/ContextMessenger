namespace ContextMessenger.Core.ProjectInfo;

using System.Text.Json.Serialization;

public sealed record ProjectInfo
{
    [JsonPropertyName("rootPath")]
    public string RootPath { get; init; } = ".";

    [JsonPropertyName("solutionFiles")]
    public IReadOnlyList<string> SolutionFiles { get; init; } = [];

    [JsonPropertyName("projectFiles")]
    public IReadOnlyList<ProjectFileInfo> ProjectFiles { get; init; } = [];

    [JsonPropertyName("testProjects")]
    public IReadOnlyList<string> TestProjects { get; init; } = [];

    [JsonPropertyName("sdkVersion")]
    public string? SdkVersion { get; init; }

    [JsonPropertyName("git")]
    public GitInfo? Git { get; init; }
}
