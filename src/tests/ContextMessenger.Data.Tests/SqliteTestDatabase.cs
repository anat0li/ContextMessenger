using ContextMessenger.Data;
using Microsoft.Data.Sqlite;

namespace ContextMessenger.Data.Tests;

internal sealed class SqliteTestDatabase : IDisposable
{
    private readonly string path;

    private SqliteTestDatabase(string path)
    {
        this.path = path;
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path
        }.ToString();
    }

    public string ConnectionString { get; }

    public static SqliteTestDatabase Create()
    {
        var path = Path.Combine(Path.GetTempPath(), $"contextmessenger-data-{Guid.NewGuid():N}.db");
        var database = new SqliteTestDatabase(path);
        database.Seed();
        return database;
    }

    public SqliteConnection OpenReadOnly()
    {
        var factory = new DataConnectionFactory(new ReflectionDataProviderResolver());
        return (SqliteConnection)factory.OpenConnection(new DataProviderSettings
        {
            ProviderInvariantName = "Microsoft.Data.Sqlite"
        }, new DataConnectionSettings
        {
            ConnectionString = ConnectionString,
            ReadOnly = true
        });
    }

    public void Dispose()
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void Seed()
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using var create = connection.CreateCommand();
        create.CommandText = """
            create table People (
                Id integer primary key,
                Name text not null,
                Bio text not null,
                Payload blob not null,
                CreatedUtc text not null,
                Price text not null,
                ExternalId text not null,
                OptionalValue text null
            );
            """;
        create.ExecuteNonQuery();

        InsertPerson(connection, 1, "Ada", new string('a', 100), [1, 2, 3, 4, 5], "2026-01-02T03:04:05.0000000Z", "12.34", "11111111-1111-1111-1111-111111111111");
        InsertPerson(connection, 2, "Linus", "short", [9, 8, 7], "2026-02-03T04:05:06.0000000Z", "56.78", "22222222-2222-2222-2222-222222222222");
    }

    private static void InsertPerson(
        SqliteConnection connection,
        long id,
        string name,
        string bio,
        byte[] payload,
        string createdUtc,
        string price,
        string externalId)
    {
        using var insert = connection.CreateCommand();
        insert.CommandText = """
            insert into People(Id, Name, Bio, Payload, CreatedUtc, Price, ExternalId, OptionalValue)
            values ($id, $name, $bio, $payload, $createdUtc, $price, $externalId, null);
            """;
        insert.Parameters.AddWithValue("$id", id);
        insert.Parameters.AddWithValue("$name", name);
        insert.Parameters.AddWithValue("$bio", bio);
        insert.Parameters.AddWithValue("$payload", payload);
        insert.Parameters.AddWithValue("$createdUtc", createdUtc);
        insert.Parameters.AddWithValue("$price", price);
        insert.Parameters.AddWithValue("$externalId", externalId);
        insert.ExecuteNonQuery();
    }
}
