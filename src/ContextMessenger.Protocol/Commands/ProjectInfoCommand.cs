using System.Text.Json.Serialization;
using ContextMessenger.Core.FileSystem;
using ContextMessenger.Core.ProjectInfo;
using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.Protocol.Commands;

public sealed class ProjectInfoCommandParams
{
}

public sealed class ProjectInfoCommandResult
{
    [JsonPropertyName("rootPath")]
    public string RootPath { get; set; } = ".";

    [JsonPropertyName("solutionFiles")]
    public IReadOnlyList<string> SolutionFiles { get; set; } = [];

    [JsonPropertyName("projectFiles")]
    public IReadOnlyList<ProjectFileInfo> ProjectFiles { get; set; } = [];

    [JsonPropertyName("testProjects")]
    public IReadOnlyList<string> TestProjects { get; set; } = [];

    [JsonPropertyName("sdkVersion")]
    public string? SdkVersion { get; set; }

    [JsonPropertyName("git")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GitInfo? Git { get; set; }
}

internal sealed class ProjectInfoHandler : CommandHandlerBase<ProjectInfoCommandParams, ProjectInfoCommandResult>
{
    private readonly IFileSystemService _fs;

    public ProjectInfoHandler(IFileSystemService fs)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
    }

    public override string CommandType => CommandTypes.ProjectInfo;

    protected override ProjectInfoCommandResult ExecuteCore(ProjectInfoCommandParams parameters)
    {
        var info = _fs.GetProjectInfo();
        return new ProjectInfoCommandResult
        {
            RootPath = info.RootPath,
            SolutionFiles = info.SolutionFiles,
            ProjectFiles = info.ProjectFiles,
            TestProjects = info.TestProjects,
            SdkVersion = info.SdkVersion,
            Git = info.Git,
        };
    }
}
