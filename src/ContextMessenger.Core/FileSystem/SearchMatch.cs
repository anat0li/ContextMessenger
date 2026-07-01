namespace ContextMessenger.Core.FileSystem;

public sealed record SearchMatch(
    string RelativePath,
    int LineNumber,
    string LineText,
    int ColumnStart,
    int ColumnEnd);
