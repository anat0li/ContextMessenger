using ContextMessenger.Data;

namespace ContextMessenger.Data.Tests;

public sealed class DataSchemaReaderTests
{
    [Fact]
    public void ReadSchema_ReturnsTablesAndColumnsForSqliteDatabase()
    {
        using var database = SqliteTestDatabase.Create();
        using var connection = database.OpenReadOnly();
        var reader = new DataSchemaReader();

        var schema = reader.ReadSchema(connection);

        Assert.Contains(schema.Collections, name => name.Equals("Tables", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(schema.Tables, table => table.Name.Equals("People", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(schema.Columns, column =>
            column.TableName?.Equals("People", StringComparison.OrdinalIgnoreCase) == true
            && column.Name.Equals("Name", StringComparison.OrdinalIgnoreCase));
    }
}
