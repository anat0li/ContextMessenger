using System.Data.Common;
using System.Text.Json.Serialization;
using ContextMessenger.Data;
using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.Protocol.Commands;

public sealed class SqlSchemaCommandParams
{
}

public sealed class SqlSchemaCommandResult
{
    [JsonPropertyName("collections")]
    public IReadOnlyList<string> Collections { get; init; } = [];

    [JsonPropertyName("tables")]
    public IReadOnlyList<DataTableInfo> Tables { get; init; } = [];

    [JsonPropertyName("columns")]
    public IReadOnlyList<DataColumnInfo> Columns { get; init; } = [];
}

public sealed class SqlTablesCommandParams
{
    [JsonPropertyName("catalog")]
    public string? Catalog { get; init; }

    [JsonPropertyName("schema")]
    public string? Schema { get; init; }
}

public sealed class SqlTablesCommandResult
{
    [JsonPropertyName("tables")]
    public IReadOnlyList<DataTableInfo> Tables { get; init; } = [];
}

public sealed class SqlColumnsCommandParams
{
    [JsonPropertyName("table")]
    public string Table { get; init; } = "";

    [JsonPropertyName("catalog")]
    public string? Catalog { get; init; }

    [JsonPropertyName("schema")]
    public string? Schema { get; init; }
}

public sealed class SqlColumnsCommandResult
{
    [JsonPropertyName("columns")]
    public IReadOnlyList<DataColumnInfo> Columns { get; init; } = [];
}

public sealed class SqlQueryCommandParams
{
    [JsonPropertyName("sql")]
    public string Sql { get; init; } = "";

    [JsonPropertyName("offset")]
    public int Offset { get; init; }

    [JsonPropertyName("limit")]
    public int? Limit { get; init; }
}

public sealed class SqlQueryColumnResult
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("dataType")]
    public string? DataType { get; init; }
}

public sealed class SqlQueryPageResult
{
    [JsonPropertyName("offset")]
    public int Offset { get; init; }

    [JsonPropertyName("limit")]
    public int Limit { get; init; }

    [JsonPropertyName("returnedRows")]
    public int ReturnedRows { get; init; }

    [JsonPropertyName("hasPreviousPage")]
    public bool HasPreviousPage { get; init; }

    [JsonPropertyName("hasNextPage")]
    public bool HasNextPage { get; init; }
}

public sealed class SqlTruncatedCellResult
{
    [JsonPropertyName("value")]
    public object? Value { get; init; }

    [JsonPropertyName("truncated")]
    public bool Truncated => true;

    [JsonPropertyName("byteSize")]
    public int? ByteSize { get; init; }
}

public sealed class SqlQueryCommandResult
{
    [JsonPropertyName("columns")]
    public IReadOnlyList<SqlQueryColumnResult> Columns { get; init; } = [];

    [JsonPropertyName("rows")]
    public IReadOnlyList<IReadOnlyList<object?>> Rows { get; init; } = [];

    [JsonPropertyName("rowCount")]
    public int RowCount { get; init; }

    [JsonPropertyName("truncated")]
    public bool Truncated { get; init; }

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; init; }

    [JsonPropertyName("page")]
    public SqlQueryPageResult Page { get; init; } = new();
}

internal abstract class SqlHandlerBase<TParams, TResult>(IDataRootSession session)
    : CommandHandlerBase<TParams, TResult>
    where TParams : new()
{
    protected IDataRootSession Session { get; } =
        session ?? throw new ArgumentNullException(nameof(session));

    protected TResult ExecuteSql(Func<TResult> action, string failureCode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return action();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ReadOnlySqlException ex)
        {
            throw new ProtocolException(ProtocolErrorCodes.SqlNotReadOnly, ex.Message, ex);
        }
        catch (DataProviderException ex)
        {
            throw new ProtocolException(
                ProtocolErrorCodes.SqlProviderNotFound,
                "The configured ADO.NET provider could not be loaded.",
                ex);
        }
        catch (DataConnectionException ex)
        {
            throw new ProtocolException(
                ProtocolErrorCodes.SqlConnectionFailed,
                "The configured database connection could not be opened.",
                ex);
        }
        catch (TimeoutException ex)
        {
            throw new ProtocolException(ProtocolErrorCodes.SqlTimeout, "The database operation timed out.", ex);
        }
        catch (DbException ex) when (IsTimeout(ex))
        {
            throw new ProtocolException(ProtocolErrorCodes.SqlTimeout, "The database operation timed out.", ex);
        }
        catch (DbException ex)
        {
            throw new ProtocolException(failureCode, "The database operation failed.", ex);
        }
    }

    private static bool IsTimeout(DbException exception) =>
        exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase);
}

internal sealed class SqlSchemaHandler(IDataRootSession session)
    : SqlHandlerBase<SqlSchemaCommandParams, SqlSchemaCommandResult>(session)
{
    public override string CommandType => CommandTypes.SqlSchema;

    protected override SqlSchemaCommandResult ExecuteCore(
        SqlSchemaCommandParams parameters,
        CancellationToken cancellationToken) =>
        ExecuteSql(() =>
        {
            var schema = Session.ReadSchema(cancellationToken);
            return new SqlSchemaCommandResult
            {
                Collections = schema.Collections,
                Tables = schema.Tables,
                Columns = schema.Columns,
            };
        }, ProtocolErrorCodes.SqlSchemaUnavailable, cancellationToken);
}

internal sealed class SqlTablesHandler(IDataRootSession session)
    : SqlHandlerBase<SqlTablesCommandParams, SqlTablesCommandResult>(session)
{
    public override string CommandType => CommandTypes.SqlTables;

    protected override SqlTablesCommandResult ExecuteCore(
        SqlTablesCommandParams parameters,
        CancellationToken cancellationToken) =>
        ExecuteSql(() =>
        {
            var tables = Session.ReadSchema(cancellationToken).Tables
                .Where(table => Matches(table.Catalog, parameters.Catalog))
                .Where(table => Matches(table.Schema, parameters.Schema))
                .ToArray();
            return new SqlTablesCommandResult { Tables = tables };
        }, ProtocolErrorCodes.SqlSchemaUnavailable, cancellationToken);

    private static bool Matches(string? value, string? filter) =>
        string.IsNullOrWhiteSpace(filter) ||
        string.Equals(value, filter, StringComparison.OrdinalIgnoreCase);
}

internal sealed class SqlColumnsHandler(IDataRootSession session)
    : SqlHandlerBase<SqlColumnsCommandParams, SqlColumnsCommandResult>(session)
{
    public override string CommandType => CommandTypes.SqlColumns;

    protected override SqlColumnsCommandResult ExecuteCore(
        SqlColumnsCommandParams parameters,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(parameters.Table))
            throw new ProtocolException(ProtocolErrorCodes.InvalidParameters, "Table name is required.");

        return ExecuteSql(() =>
        {
            var schema = Session.ReadSchema(cancellationToken);
            var tables = schema.Tables
                .Where(table => string.Equals(
                    table.Name,
                    parameters.Table,
                    StringComparison.OrdinalIgnoreCase))
                .Where(table => Matches(table.Catalog, parameters.Catalog))
                .Where(table => Matches(table.Schema, parameters.Schema))
                .ToArray();

            if (tables.Length == 0)
            {
                throw new ProtocolException(
                    ProtocolErrorCodes.SqlTableNotFound,
                    BuildTableNotFoundMessage(parameters));
            }

            var columns = schema.Columns
                .Where(column => tables.Any(table => IsColumnForTable(column, table)))
                .ToArray();
            return new SqlColumnsCommandResult { Columns = columns };
        }, ProtocolErrorCodes.SqlSchemaUnavailable, cancellationToken);
    }

    private static bool IsColumnForTable(DataColumnInfo column, DataTableInfo table) =>
        string.Equals(column.TableName, table.Name, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(column.Catalog, table.Catalog, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(column.Schema, table.Schema, StringComparison.OrdinalIgnoreCase);

    private static string BuildTableNotFoundMessage(SqlColumnsCommandParams parameters)
    {
        var scope = new List<string>();
        if (!string.IsNullOrWhiteSpace(parameters.Catalog))
            scope.Add($"catalog '{parameters.Catalog}'");
        if (!string.IsNullOrWhiteSpace(parameters.Schema))
            scope.Add($"schema '{parameters.Schema}'");

        return scope.Count == 0
            ? $"Table '{parameters.Table}' was not found."
            : $"Table '{parameters.Table}' was not found in {string.Join(" and ", scope)}.";
    }

    private static bool Matches(string? value, string? filter) =>
        string.IsNullOrWhiteSpace(filter) ||
        string.Equals(value, filter, StringComparison.OrdinalIgnoreCase);
}

internal sealed class SqlQueryHandler(IDataRootSession session)
    : SqlHandlerBase<SqlQueryCommandParams, SqlQueryCommandResult>(session)
{
    public override string CommandType => CommandTypes.SqlQuery;

    protected override SqlQueryCommandResult ExecuteCore(
        SqlQueryCommandParams parameters,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(parameters.Sql))
            throw new ProtocolException(ProtocolErrorCodes.InvalidParameters, "SQL text is required.");

        return ExecuteSql(() =>
        {
            var result = Session.ExecuteQuery(
                parameters.Sql,
                new DataQueryPageRequest(parameters.Offset, parameters.Limit),
                cancellationToken);
            return new SqlQueryCommandResult
            {
                Columns = result.Columns
                    .Select(column => new SqlQueryColumnResult
                    {
                        Name = column.Name,
                        DataType = column.DataType,
                    })
                    .ToArray(),
                Rows = result.Rows
                    .Select(row => (IReadOnlyList<object?>)row.Select(MapCell).ToArray())
                    .ToArray(),
                RowCount = result.RowCount,
                Truncated = result.Truncated,
                DurationMs = result.DurationMs,
                Page = new SqlQueryPageResult
                {
                    Offset = result.Page.Offset,
                    Limit = result.Page.Limit,
                    ReturnedRows = result.Page.ReturnedRows,
                    HasPreviousPage = result.Page.HasPreviousPage,
                    HasNextPage = result.Page.HasNextPage,
                },
            };
        }, ProtocolErrorCodes.SqlQueryFailed, cancellationToken);
    }

    private static object? MapCell(DataCellValue cell) =>
        cell.Truncated
            ? new SqlTruncatedCellResult { Value = cell.Value, ByteSize = cell.ByteSize }
            : cell.Value;
}
