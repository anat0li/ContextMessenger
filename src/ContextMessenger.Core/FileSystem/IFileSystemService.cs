namespace ContextMessenger.Core.FileSystem;

using ContextMessenger.Core.ProjectInfo;

public interface IFileSystemService
{
    TreeNode GetTree(TreeQuery query);

    FileContent ReadFile(
        string relativePath,
        int? startLine = null,
        int? endLine = null,
        long maxBytes = 1_048_576);

    IReadOnlyList<SearchMatch> SearchText(SearchQuery query);

    IReadOnlyList<string> ListFiles(ListFilesQuery query);

    ProjectInfo GetProjectInfo();
}
