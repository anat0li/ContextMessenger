using System.Data.Common;
using ContextMessenger.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySql.Data.MySqlClient;

namespace ContextMessenger.Data.Tests;

public sealed class ReflectionDataProviderResolverTests
{
    [Fact]
    public void Resolve_ReturnsSqliteFactory_ForKnownProvider()
    {
        var resolver = new ReflectionDataProviderResolver();

        var factory = resolver.Resolve(new DataProviderSettings
        {
            ProviderInvariantName = "Microsoft.Data.Sqlite"
        });

        Assert.Same(SqliteFactory.Instance, factory);
    }

    [Fact]
    public void Resolve_ReturnsSqlClientFactory_ForKnownProvider()
    {
        var resolver = new ReflectionDataProviderResolver();

        var factory = resolver.Resolve(new DataProviderSettings
        {
            ProviderInvariantName = "Microsoft.Data.SqlClient"
        });

        Assert.Same(SqlClientFactory.Instance, factory);
    }

    [Fact]
    public void Resolve_ReturnsMySqlFactory_ForKnownProvider()
    {
        var resolver = new ReflectionDataProviderResolver();

        var factory = resolver.Resolve(new DataProviderSettings
        {
            ProviderInvariantName = "MySql.Data"
        });

        Assert.Same(MySqlClientFactory.Instance, factory);
    }

    [Fact]
    public void Resolve_CanLoadFactoryFromAssemblyPath()
    {
        var resolver = new ReflectionDataProviderResolver();

        var factory = resolver.Resolve(new DataProviderSettings
        {
            ProviderInvariantName = "Custom.Sqlite",
            ProviderAssemblyPath = typeof(SqliteFactory).Assembly.Location,
            ProviderFactoryTypeName = typeof(SqliteFactory).FullName
        });

        Assert.Same(SqliteFactory.Instance, factory);
    }

    [Fact]
    public void Resolve_ThrowsClearException_WhenProviderCannotBeResolved()
    {
        var resolver = new ReflectionDataProviderResolver();

        var exception = Assert.Throws<DataProviderException>(() => resolver.Resolve(new DataProviderSettings
        {
            ProviderInvariantName = "Missing.Provider"
        }));

        Assert.Contains("Missing.Provider", exception.Message, StringComparison.Ordinal);
    }
}
