using System.IO;
using ContextMessenger.App.Wpf.Services;
using ContextMessenger.App.Wpf.Settings;
using ContextMessenger.Core.Meta;
using Microsoft.Data.Sqlite;

namespace ContextMessenger.App.Wpf.Tests;

public sealed class SqlSessionFactoryTests : IDisposable
{
    private readonly string databasePath =
        Path.Combine(Path.GetTempPath(), "ContextMessengerSqlRoot_" + Guid.NewGuid().ToString("N") + ".db");

    public SqlSessionFactoryTests()
    {
        SQLitePCL.Batteries_V2.Init();
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            create table Items (Id integer primary key, Name text not null);
            insert into Items (Name) values ('Alpha'), ('Beta');
            """;
        command.ExecuteNonQuery();
    }

    [Fact]
    public void Create_sql_root_builds_patchless_query_session()
    {
        var target = new TargetProfile { Name = "ChatGPT", ProcessName = "ChatGPT" };
        var root = new RootProfile
        {
            Name = "Database",
            Kind = RootKind.Sql,
            Sql = new SqlRootSettings
            {
                ProviderInvariantName = "Microsoft.Data.Sqlite",
                ConnectionStringRef = $"literal:Data Source={databasePath}",
                MaxRows = 1,
            },
        };
        var profiles = new Profiles(target, root);
        var factory = new SessionFactory(profiles, new Coordinator());

        var session = factory.Create(target, root);
        var response = session.Processor.ProcessRequestBodies(
        [
            $$"""
            {
              "version": "1.0",
              "id": "{{Guid.NewGuid()}}",
              "commands": [
                {
                  "type": "sql_query",
                  "sql": "select Id, Name from Items order by Id",
                  "offset": 0,
                  "limit": 1
                }
              ]
            }
            """
        ]);

        Assert.Null(session.Patches);
        Assert.Contains("\"Alpha\"", response.ResponseText);
        Assert.Contains("\"hasNextPage\": true", response.ResponseText);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(databasePath))
            File.Delete(databasePath);
    }

    private sealed class Profiles(TargetProfile target, RootProfile root) : IAvailableProfilesProvider
    {
        public IReadOnlyList<RootProfile> GetAvailableRoots() => [root];

        public IReadOnlyList<TargetProfile> GetAvailableTargets() => [target];
    }

    private sealed class Coordinator : IRootSwitchCoordinator
    {
        public void ActivateRootForTarget(string targetName, string rootName)
        {
        }
    }
}
