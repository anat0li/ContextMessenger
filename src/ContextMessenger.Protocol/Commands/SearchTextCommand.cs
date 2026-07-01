using System.Text.Json.Serialization;
using ContextMessenger.Core.FileSystem;
using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.Protocol.Commands;

public sealed class SearchTextCommandParams
{
    [JsonPropertyName("pattern")]
    public string Pattern { get; set; } = "";

    [JsonPropertyName("isRegex")]
    public bool IsRegex { get; set; }

    [JsonPropertyName("ignoreCase")]
    public bool IgnoreCase { get; set; } = true;

    [JsonPropertyName("path")]
    public string Path { get; set; } = ".";

    [JsonPropertyName("include")]
    public List<string>? Include { get; set; }

    [JsonPropertyName("exclude")]
    public List<string>? Exclude { get; set; }

    [JsonPropertyName("maxResults")]
    public int MaxResults { get; set; } = 500;
}

public sealed class SearchTextCommandResult
{
    [JsonPropertyName("matches")]
    public List<SearchTextMatchDto> Matches { get; set; } = new();

    [JsonPropertyName("matchCount")]
    public int MatchCount { get; set; }
}

public sealed class SearchTextMatchDto
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("line")]
    public int Line { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("columnStart")]
    public int ColumnStart { get; set; }

    [JsonPropertyName("columnEnd")]
    public int ColumnEnd { get; set; }
}

internal sealed class SearchTextHandler : CommandHandlerBase<SearchTextCommandParams, SearchTextCommandResult>
{
    private readonly IFileSystemService _fs;

    public SearchTextHandler(IFileSystemService fs)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
    }

    public override string CommandType => CommandTypes.SearchText;

    protected override SearchTextCommandResult ExecuteCore(SearchTextCommandParams p)
    {
        if (string.IsNullOrEmpty(p.Pattern))
            throw new ProtocolException(
                ProtocolErrorCodes.InvalidParameters,
                "'pattern' is required for search_text.");

        var query = new SearchQuery(p.Pattern)
        {
            IsRegex = p.IsRegex,
            IgnoreCase = p.IgnoreCase,
            RelativePath = string.IsNullOrEmpty(p.Path) ? "." : p.Path,
            IncludeGlobs = p.Include ?? [],
            ExcludeGlobs = p.Exclude ?? [],
            MaxResults = p.MaxResults,
        };

        var matches = _fs.SearchText(query);
        return new SearchTextCommandResult
        {
            MatchCount = matches.Count,
            Matches = matches.Select(m => new SearchTextMatchDto
            {
                Path = m.RelativePath,
                Line = m.LineNumber,
                Text = m.LineText,
                ColumnStart = m.ColumnStart,
                ColumnEnd = m.ColumnEnd,
            }).ToList(),
        };
    }
}
