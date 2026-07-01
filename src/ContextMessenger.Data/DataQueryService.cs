using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace ContextMessenger.Data;

public sealed class DataQueryService(IReadOnlySqlGuard readOnlySqlGuard) : IDataQueryService
{
    public DataQueryResult Execute(
        DbConnection connection,
        string sql,
        DataConnectionSettings settings,
        DataQueryPageRequest? page = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(settings);

        readOnlySqlGuard.Validate(sql);
        cancellationToken.ThrowIfCancellationRequested();
        var pageBounds = ResolvePageBounds(settings, page);

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        command.CommandTimeout = settings.CommandTimeoutSeconds;
        var cancellationState = new QueryCancellationState(command, connection);
        using var cancellationRegistration = cancellationToken.Register(static state =>
            ((QueryCancellationState)state!).Cancel(), cancellationState);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var reader = command.ExecuteReader(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess);

            var columns = Enumerable.Range(0, reader.FieldCount)
                .Select(index => new DataQueryColumn(reader.GetName(index), reader.GetDataTypeName(index)))
                .ToArray();

            var rows = new List<IReadOnlyList<DataCellValue>>();
            var cellTruncated = false;
            var hasNextPage = false;
            var rowsSeen = 0;
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (rowsSeen++ < pageBounds.Offset)
                {
                    continue;
                }

                if (rows.Count >= pageBounds.Limit)
                {
                    hasNextPage = true;
                    break;
                }

                var row = new DataCellValue[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[i] = MapCell(reader.GetValue(i), settings.MaxCellBytes);
                    cellTruncated |= row[i].Truncated;
                }

                rows.Add(row);
            }

            stopwatch.Stop();
            return new DataQueryResult(
                columns,
                rows,
                rows.Count,
                cellTruncated || hasNextPage,
                stopwatch.ElapsedMilliseconds,
                new DataQueryPageInfo(
                    pageBounds.Offset,
                    pageBounds.Limit,
                    rows.Count,
                    pageBounds.Offset > 0,
                    hasNextPage));
        }
        catch (Exception ex) when (cancellationToken.IsCancellationRequested && ex is not OperationCanceledException)
        {
            throw new OperationCanceledException("Database query execution was cancelled.", ex, cancellationToken);
        }
    }

    private static PageBounds ResolvePageBounds(DataConnectionSettings settings, DataQueryPageRequest? page)
    {
        var offset = page?.Offset ?? 0;
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(page), "Page offset cannot be negative.");
        }

        if (settings.MaxRows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "MaxRows must be greater than zero.");
        }

        var requestedLimit = page?.Limit ?? settings.MaxRows;
        if (requestedLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(page), "Page limit must be greater than zero.");
        }

        return new PageBounds(offset, Math.Min(requestedLimit, settings.MaxRows));
    }

    private static DataCellValue MapCell(object value, int maxCellBytes)
    {
        if (value is DBNull)
        {
            return new DataCellValue(null);
        }

        return value switch
        {
            byte[] bytes => MapBytes(bytes, maxCellBytes),
            DateTime dateTime => new DataCellValue(dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
            DateTimeOffset dateTimeOffset => new DataCellValue(dateTimeOffset.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
            decimal decimalValue => new DataCellValue(decimalValue.ToString(CultureInfo.InvariantCulture)),
            Guid guid => new DataCellValue(guid.ToString("D", CultureInfo.InvariantCulture)),
            string text => MapString(text, maxCellBytes),
            _ => new DataCellValue(value)
        };
    }

    private static DataCellValue MapBytes(byte[] bytes, int maxCellBytes)
    {
        if (bytes.Length <= maxCellBytes)
        {
            return new DataCellValue(Convert.ToBase64String(bytes), ByteSize: bytes.Length);
        }

        return new DataCellValue(Convert.ToBase64String(bytes[..maxCellBytes]), Truncated: true, ByteSize: bytes.Length);
    }

    private static DataCellValue MapString(string text, int maxCellBytes)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length <= maxCellBytes)
        {
            return new DataCellValue(text, ByteSize: bytes.Length);
        }

        var truncatedText = Encoding.UTF8.GetString(bytes[..maxCellBytes]);
        return new DataCellValue(truncatedText, Truncated: true, ByteSize: bytes.Length);
    }

    private sealed record PageBounds(int Offset, int Limit);

    private sealed class QueryCancellationState(DbCommand command, DbConnection connection)
    {
        private int cancellationStarted;

        public void Cancel()
        {
            if (Interlocked.Exchange(ref cancellationStarted, 1) != 0)
            {
                return;
            }

            try
            {
                command.Cancel();
            }
            catch (Exception)
            {
            }

            if (connection is SqliteConnection sqliteConnection)
            {
                try
                {
                    raw.sqlite3_interrupt(sqliteConnection.Handle);
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
