using System.Text.Json.Serialization;
using ContextMessenger.Core.FileSystem;
using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.Protocol.Commands;

public sealed class ReadFileCommandParams
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("startLine")]
    public int? StartLine { get; set; }

    [JsonPropertyName("endLine")]
    public int? EndLine { get; set; }

    [JsonPropertyName("maxBytes")]
    public long MaxBytes { get; set; } = 1_048_576;
}

public sealed class ReadFileCommandResult
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("lineCount")]
    public int LineCount { get; set; }

    [JsonPropertyName("byteSize")]
    public long ByteSize { get; set; }

    [JsonPropertyName("isTruncated")]
    public bool IsTruncated { get; set; }

    [JsonPropertyName("contentHash")]
    public string ContentHash { get; set; } = "";

    [JsonPropertyName("rangeHash")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RangeHash { get; set; }

    [JsonPropertyName("rangeStartLine")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RangeStartLine { get; set; }

    [JsonPropertyName("rangeEndLine")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RangeEndLine { get; set; }

    [JsonPropertyName("rangeIncludesEndLineTerminator")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? RangeIncludesEndLineTerminator { get; set; }

    [JsonPropertyName("lineEnding")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LineEnding { get; set; }
}

internal sealed class ReadFileHandler : CommandHandlerBase<ReadFileCommandParams, ReadFileCommandResult>
{
    private readonly IFileSystemService _fs;

    public ReadFileHandler(IFileSystemService fs)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
    }

    public override string CommandType => CommandTypes.ReadFile;

    protected override ReadFileCommandResult ExecuteCore(ReadFileCommandParams p)
    {
        if (string.IsNullOrWhiteSpace(p.Path))
            throw new ProtocolException(
                ProtocolErrorCodes.InvalidParameters,
                "'path' is required for read_file.");

        var content = _fs.ReadFile(p.Path, p.StartLine, p.EndLine, p.MaxBytes);
        return new ReadFileCommandResult
        {
            Path = content.RelativePath,
            Content = content.Content,
            LineCount = content.LineCount,
            ByteSize = content.ByteSize,
            IsTruncated = content.IsTruncated,
            ContentHash = content.ContentHash,
            RangeHash = content.RangeHash,
            RangeStartLine = content.RangeStartLine,
            RangeEndLine = content.RangeEndLine,
            RangeIncludesEndLineTerminator = content.RangeIncludesEndLineTerminator,
            LineEnding = content.LineEnding,
        };
    }
}
