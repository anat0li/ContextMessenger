using ContextMessenger.App.Wpf.Settings;
using ContextMessenger.Data;

namespace ContextMessenger.App.Wpf.Services;

public sealed class SqlConnectionTester : ISqlConnectionTester
{
    private readonly IDataConnectionFactory _connectionFactory;
    private readonly ISqlConnectionStringResolver _connectionStringResolver;

    public SqlConnectionTester(
        IDataConnectionFactory? connectionFactory = null,
        ISqlConnectionStringResolver? connectionStringResolver = null)
    {
        _connectionFactory = connectionFactory
            ?? new DataConnectionFactory(new ReflectionDataProviderResolver());
        _connectionStringResolver = connectionStringResolver ?? new SqlConnectionStringResolver();
    }

    public void Test(RootProfile root)
    {
        var providerSettings = SqlRootConnectionOptions.CreateProviderSettings(root);
        var connectionSettings = SqlRootConnectionOptions.CreateConnectionSettings(root, _connectionStringResolver);

        using var connection = _connectionFactory.OpenConnection(providerSettings, connectionSettings);
    }
}
