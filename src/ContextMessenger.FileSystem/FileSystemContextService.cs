using ContextMessenger.Core.FileSystem;
using ContextMessenger.Core.ProjectInfo;

namespace ContextMessenger.FileSystem;

public sealed class FileSystemContextService : IFileSystemService
{
    private readonly RepoTreeProvider _tree;
    private readonly FileReaderService _reader;
    private readonly TextSearchService _search;
    private readonly ProjectInfoService _projectInfo;

    public FileSystemContextService(string rootPath)
        : this(new PathSandbox(rootPath))
    {
    }

    public FileSystemContextService(PathSandbox sandbox)
    {
        ArgumentNullException.ThrowIfNull(sandbox);
        Sandbox = sandbox;
        _tree = new RepoTreeProvider(sandbox);
        _reader = new FileReaderService(sandbox);
        _search = new TextSearchService(sandbox);
        _projectInfo = new ProjectInfoService(sandbox);
    }

    public PathSandbox Sandbox { get; }

    public TreeNode GetTree(TreeQuery query) => _tree.GetTree(query);

    public FileContent ReadFile(string relativePath, int? startLine = null, int? endLine = null, long maxBytes = 1_048_576)
        => _reader.ReadFile(relativePath, startLine, endLine, maxBytes);

    public IReadOnlyList<SearchMatch> SearchText(SearchQuery query) => _search.SearchText(query);

    public IReadOnlyList<string> ListFiles(ListFilesQuery query) => _search.ListFiles(query);

    public ProjectInfo GetProjectInfo() => _projectInfo.GetProjectInfo();
}
