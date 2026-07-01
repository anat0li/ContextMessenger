using ContextMessenger.Data;

namespace ContextMessenger.Data.Tests;

public sealed class DataConnectionFactoryTests
{
    [Fact]
    public void OpenConnection_AppliesSqliteReadOnlyMode()
    {
        using var database = SqliteTestDatabase.Create();
        var factory = new DataConnectionFactory(new ReflectionDataProviderResolver());

        using var connection = factory.OpenConnection(new DataProviderSettings
        {
            ProviderInvariantName = "Microsoft.Data.Sqlite"
        }, new DataConnectionSettings
        {
            ConnectionString = database.ConnectionString,
            ReadOnly = true
        });

        Assert.Equal(System.Data.ConnectionState.Open, connection.State);
        Assert.ThrowsAny<Exception>(() =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "insert into People(Name) values ('Grace')";
            command.ExecuteNonQuery();
        });
    }
}
