using ContextMessenger.App.Wpf.ViewModels;

namespace ContextMessenger.App.Wpf.Patching;

/// <summary>
/// Turns a flat list of changed files into a Solution-Explorer-style folder/file tree by splitting
/// each path on '/'. Folders sort before files, both alphabetically (case-insensitive).
/// </summary>
public static class PatchTreeBuilder
{
    public static IReadOnlyList<PatchTreeNode> Build(IReadOnlyList<PatchReviewFile> files)
    {
        var root = new FolderBuilder("");

        foreach (var file in files)
        {
            var segments = file.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                continue;

            var folder = root;
            for (var i = 0; i < segments.Length - 1; i++)
                folder = folder.GetOrAddFolder(segments[i]);

            folder.Files.Add((segments[^1], file));
        }

        return root.ToNodes();
    }

    private sealed class FolderBuilder(string name)
    {
        private readonly SortedDictionary<string, FolderBuilder> _folders = new(StringComparer.OrdinalIgnoreCase);

        public string Name { get; } = name;

        public List<(string Leaf, PatchReviewFile File)> Files { get; } = [];

        public FolderBuilder GetOrAddFolder(string segment)
        {
            if (!_folders.TryGetValue(segment, out var child))
            {
                child = new FolderBuilder(segment);
                _folders[segment] = child;
            }

            return child;
        }

        public IReadOnlyList<PatchTreeNode> ToNodes()
        {
            var nodes = new List<PatchTreeNode>();

            foreach (var folder in _folders.Values)
            {
                nodes.Add(new PatchTreeNode
                {
                    Name = folder.Name,
                    IsFolder = true,
                    Children = folder.ToNodes(),
                });
            }

            foreach (var (leaf, file) in Files.OrderBy(f => f.Leaf, StringComparer.OrdinalIgnoreCase))
            {
                nodes.Add(new PatchTreeNode
                {
                    Name = leaf,
                    IsFolder = false,
                    RelativePath = file.Path,
                    Operation = file.Operation,
                });
            }

            return nodes;
        }
    }
}
