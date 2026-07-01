using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ContextMessenger.App.Wpf.ViewModels;

/// <summary>
/// One node in the changed-files tree (left panel). Folders group children; files carry the
/// repo-relative <see cref="RelativePath"/> and <see cref="Operation"/> that drive the diff and the
/// status colour.
/// </summary>
public sealed partial class PatchTreeNode : ObservableObject
{
    private const string FolderIcon = "pack://application:,,,/Resources/Images/folder.svg";
    private const string CodeIcon = "pack://application:,,,/Resources/Images/file-code.svg";
    private const string FileIcon = "pack://application:,,,/Resources/Images/file.svg";

    /// <summary>Two-way bound to the tree item's selection so the auto-selected file is highlighted.</summary>
    [ObservableProperty]
    private bool _isSelected;

    public required string Name { get; init; }

    public bool IsFolder { get; init; }

    /// <summary>Repo-relative path (forward slashes); set for file nodes only.</summary>
    public string? RelativePath { get; init; }

    /// <summary>One of <c>create</c>/<c>replace</c>/<c>delete</c>; set for file nodes only.</summary>
    public string Operation { get; init; } = "";

    public IReadOnlyList<PatchTreeNode> Children { get; init; } = [];

    public bool IsExpanded { get; init; } = true;

    public Uri IconUri => new(IsFolder ? FolderIcon : IconForFile(RelativePath));

    private static string IconForFile(string? path)
    {
        var ext = Path.GetExtension(path ?? "").ToLowerInvariant();
        return ext switch
        {
            ".cs" or ".xaml" or ".axaml" or ".xml" or ".csproj" or ".props" or ".targets"
                or ".json" or ".js" or ".ts" or ".slnx" or ".md" => CodeIcon,
            _ => FileIcon,
        };
    }
}
