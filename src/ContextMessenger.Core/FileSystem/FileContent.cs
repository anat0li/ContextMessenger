namespace ContextMessenger.Core.FileSystem;

public sealed record FileContent(
    string RelativePath,
    string Content,
    int LineCount,
    long ByteSize,
    bool IsTruncated,
    string ContentHash,
    string? RangeHash = null,
    int? RangeStartLine = null,
    int? RangeEndLine = null,
    bool? RangeIncludesEndLineTerminator = null,
    string? LineEnding = null);
