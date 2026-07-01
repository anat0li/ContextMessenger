using System.Text.Json.Serialization;
using ContextMessenger.Core.FileSystem;
using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.Protocol.Commands;

public sealed class TreeCommandParams
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = ".";

    [JsonPropertyName("depth")]
    public int Depth { get; set; } = 3;

    [JsonPropertyName("include")]
    public List<string>? Include { get; set; }

    [JsonPropertyName("exclude")]
    public List<string>? Exclude { get; set; }
}

public sealed class TreeCommandResult
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";
}

internal sealed class TreeHandler : CommandHandlerBase<TreeCommandParams, TreeCommandResult>
{
    private readonly IFileSystemService _fs;

    public TreeHandler(IFileSystemService fs)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
    }

    public override string CommandType => CommandTypes.Tree;

    protected override TreeCommandResult ExecuteCore(TreeCommandParams p)
    {
        var query = new TreeQuery(string.IsNullOrEmpty(p.Path) ? "." : p.Path)
        {
            MaxDepth = p.Depth,
            IncludeGlobs = p.Include ?? [],
            ExcludeGlobs = p.Exclude ?? [],
        };
        var node = _fs.GetTree(query);
        return new TreeCommandResult
        {
            Path = node.RelativePath,
            Content = TreeRenderer.Render(node),
        };
    }
}
