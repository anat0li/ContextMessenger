namespace ContextMessenger.Data;

public sealed record DataQueryColumn(string Name, string? DataType);

public sealed record DataCellValue(object? Value, bool Truncated = false, int? ByteSize = null);

public sealed record DataQueryPageRequest(int Offset = 0, int? Limit = null);

public sealed record DataQueryPageInfo(
    int Offset,
    int Limit,
    int ReturnedRows,
    bool HasPreviousPage,
    bool HasNextPage);

public sealed record DataQueryResult(
    IReadOnlyList<DataQueryColumn> Columns,
    IReadOnlyList<IReadOnlyList<DataCellValue>> Rows,
    int RowCount,
    bool Truncated,
    long DurationMs,
    DataQueryPageInfo Page);
