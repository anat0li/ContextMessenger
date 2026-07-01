namespace ContextMessenger.Core.ProjectInfo;

public interface IGitRepositoryInfoProvider
{
    GitInfo GetGitInfo(string rootPath);
}
