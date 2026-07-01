using System.Text.Json.Serialization;
using ContextMessenger.Core.Patching;
using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.Protocol.Commands;

public sealed class GitStatusCommandResult
{
    [JsonPropertyName("isRepository")]
    public bool IsRepository { get; set; }

    [JsonPropertyName("isClean")]
    public bool IsClean { get; set; }

    [JsonPropertyName("branch")]
    public string? Branch { get; set; }

    [JsonPropertyName("headSha")]
    public string? HeadSha { get; set; }

    [JsonPropertyName("changedFiles")]
    public IReadOnlyList<GitStatusFileResult> ChangedFiles { get; set; } = [];
}

public sealed class GitStatusFileResult
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";
}

internal sealed class GitStatusHandler : CommandHandlerBase<object, GitStatusCommandResult>
{
    private readonly IGitStatusService _git;

    public GitStatusHandler(IGitStatusService git)
    {
        _git = git ?? throw new ArgumentNullException(nameof(git));
    }

    public override string CommandType => CommandTypes.GitStatus;

    protected override GitStatusCommandResult ExecuteCore(object parameters)
    {
        var status = _git.GetStatus();
        return new GitStatusCommandResult
        {
            IsRepository = status.IsRepository,
            IsClean = status.IsClean,
            Branch = status.Branch,
            HeadSha = status.HeadSha,
            ChangedFiles = status.ChangedFiles
                .Select(f => new GitStatusFileResult { Path = f.Path, Status = f.Status })
                .ToArray(),
        };
    }
}
