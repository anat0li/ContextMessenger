using System.Text.Json.Serialization;
using ContextMessenger.Core.FileSystem;
using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.Protocol.Commands;

public sealed class ListFilesCommandParams
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = ".";

    [JsonPropertyName("include")]
    public List<string>? Include { get; set; }

    [JsonPropertyName("exclude")]
    public List<string>? Exclude { get; set; }

    [JsonPropertyName("maxResults")]
    public int MaxResults { get; set; } = 5000;
}

public sealed class ListFilesCommandResult
{
    [JsonPropertyName("files")]
    public List<string> Files { get; set; } = new();

    [JsonPropertyName("fileCount")]
    public int FileCount { get; set; }
}

internal sealed class ListFilesHandler : CommandHandlerBase<ListFilesCommandParams, ListFilesCommandResult>
{
    private readonly IFileSystemService _fs;

    public ListFilesHandler(IFileSystemService fs)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
    }

    public override string CommandType => CommandTypes.ListFiles;

    protected override ListFilesCommandResult ExecuteCore(ListFilesCommandParams p)
    {
        var query = new ListFilesQuery
        {
            RelativePath = string.IsNullOrEmpty(p.Path) ? "." : p.Path,
            IncludeGlobs = p.Include ?? [],
            ExcludeGlobs = p.Exclude ?? [],
            MaxResults = p.MaxResults,
        };

        var files = _fs.ListFiles(query);
        return new ListFilesCommandResult
        {
            FileCount = files.Count,
            Files = files.ToList(),
        };
    }
}
