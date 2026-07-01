namespace ContextMessenger.Core.FileSystem;

public sealed record TreeNode(
    string Name,
    string RelativePath,
    bool IsDirectory,
    IReadOnlyList<TreeNode> Children);
