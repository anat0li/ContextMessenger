using System.Data;
using System.Data.Common;
using System.Globalization;

namespace ContextMessenger.Data;

public sealed class DataSchemaReader : IDataSchemaReader
{
    public DataSchemaInfo ReadSchema(DbConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var collections = ReadCollectionNames(connection).ToList();
        var tables = collections.Contains("Tables", StringComparer.OrdinalIgnoreCase)
            ? ReadTables(connection.GetSchema("Tables"))
            : [];
        var columns = collections.Contains("Columns", StringComparer.OrdinalIgnoreCase)
            ? ReadColumns(connection.GetSchema("Columns"))
            : [];

        if (tables.Count == 0 && IsSqliteConnection(connection))
        {
            tables = ReadSqliteTables(connection);
            columns = ReadSqliteColumns(connection, tables);
            AddCollectionIfMissing(collections, "Tables");
            AddCollectionIfMissing(collections, "Columns");
        }

        return new DataSchemaInfo(collections, tables, columns);
    }

    private static IReadOnlyList<string> ReadCollectionNames(DbConnection connection)
    {
        using var table = connection.GetSchema();
        return table.Rows
            .Cast<DataRow>()
            .Select(row => GetString(row, "CollectionName"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<DataTableInfo> ReadTables(DataTable table)
    {
        using (table)
        {
            return table.Rows
                .Cast<DataRow>()
                .Select(row => new DataTableInfo(
                    GetString(row, "TABLE_CATALOG"),
                    GetString(row, "TABLE_SCHEMA"),
                    GetString(row, "TABLE_NAME") ?? GetString(row, "table_name") ?? "",
                    GetString(row, "TABLE_TYPE") ?? GetString(row, "table_type")))
                .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    private static IReadOnlyList<DataColumnInfo> ReadColumns(DataTable table)
    {
        using (table)
        {
            return table.Rows
                .Cast<DataRow>()
                .Select(row => new DataColumnInfo(
                    GetString(row, "TABLE_CATALOG"),
                    GetString(row, "TABLE_SCHEMA"),
                    GetString(row, "TABLE_NAME"),
                    GetString(row, "COLUMN_NAME") ?? GetString(row, "column_name") ?? "",
                    GetString(row, "DATA_TYPE") ?? GetString(row, "data_type"),
                    GetInt32(row, "ORDINAL_POSITION"),
                    GetBoolean(row, "IS_NULLABLE")))
                .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                .OrderBy(item => item.TableName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Ordinal)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    private static string? GetString(DataRow row, string columnName)
    {
        if (!row.Table.Columns.Contains(columnName) || row[columnName] is DBNull)
        {
            return null;
        }

        return Convert.ToString(row[columnName], CultureInfo.InvariantCulture);
    }

    private static int? GetInt32(DataRow row, string columnName)
    {
        if (!row.Table.Columns.Contains(columnName) || row[columnName] is DBNull)
        {
            return null;
        }

        return Convert.ToInt32(row[columnName], CultureInfo.InvariantCulture);
    }

    private static bool? GetBoolean(DataRow row, string columnName)
    {
        var value = GetString(row, columnName);
        return value?.ToUpperInvariant() switch
        {
            "YES" or "TRUE" or "1" => true,
            "NO" or "FALSE" or "0" => false,
            _ => null
        };
    }

    private static bool IsSqliteConnection(DbConnection connection)
    {
        return connection.GetType().FullName?.Equals("Microsoft.Data.Sqlite.SqliteConnection", StringComparison.Ordinal) == true;
    }

    private static IReadOnlyList<DataTableInfo> ReadSqliteTables(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            select name, type
            from sqlite_schema
            where type in ('table', 'view')
              and name not like 'sqlite_%'
            order by name
            """;

        using var reader = command.ExecuteReader();
        var tables = new List<DataTableInfo>();
        while (reader.Read())
        {
            tables.Add(new DataTableInfo(
                Catalog: null,
                Schema: null,
                Name: reader.GetString(0),
                Type: reader.GetString(1)));
        }

        return tables;
    }

    private static IReadOnlyList<DataColumnInfo> ReadSqliteColumns(DbConnection connection, IReadOnlyList<DataTableInfo> tables)
    {
        var columns = new List<DataColumnInfo>();
        foreach (var table in tables)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"pragma table_info(\"{table.Name.Replace("\"", "\"\"", StringComparison.Ordinal)}\")";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(new DataColumnInfo(
                    Catalog: null,
                    Schema: null,
                    TableName: table.Name,
                    Name: reader.GetString(reader.GetOrdinal("name")),
                    DataType: reader.GetString(reader.GetOrdinal("type")),
                    Ordinal: reader.GetInt32(reader.GetOrdinal("cid")) + 1,
                    IsNullable: reader.GetInt32(reader.GetOrdinal("notnull")) == 0));
            }
        }

        return columns;
    }

    private static void AddCollectionIfMissing(List<string> collections, string name)
    {
        if (!collections.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            collections.Add(name);
        }
    }
}
