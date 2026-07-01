using System.Data.Common;

namespace ContextMessenger.Data;

public sealed class DataConnectionFactory(IDataProviderResolver providerResolver) : IDataConnectionFactory
{
    public DbConnection OpenConnection(DataProviderSettings providerSettings, DataConnectionSettings connectionSettings)
    {
        ArgumentNullException.ThrowIfNull(connectionSettings);

        var factory = providerResolver.Resolve(providerSettings);
        var connection = factory.CreateConnection()
            ?? throw new DataProviderException($"Provider '{providerSettings.ProviderInvariantName}' did not create a connection.");

        try
        {
            connection.ConnectionString = BuildConnectionString(providerSettings, connectionSettings);
            connection.Open();
            return connection;
        }
        catch (Exception ex)
        {
            connection.Dispose();
            throw new DataConnectionException(
                $"Could not open a connection using provider '{providerSettings.ProviderInvariantName}'.",
                ex);
        }
    }

    private static string BuildConnectionString(DataProviderSettings providerSettings, DataConnectionSettings connectionSettings)
    {
        if (!connectionSettings.ReadOnly)
        {
            return connectionSettings.ConnectionString;
        }

        if (providerSettings.ProviderInvariantName.Equals("Microsoft.Data.Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            var builder = new DbConnectionStringBuilder
            {
                ConnectionString = connectionSettings.ConnectionString
            };

            if (!builder.ContainsKey("Mode"))
            {
                builder["Mode"] = "ReadOnly";
            }

            return builder.ConnectionString;
        }

        if (providerSettings.ProviderInvariantName.Equals("Microsoft.Data.SqlClient", StringComparison.OrdinalIgnoreCase))
        {
            var builder = new DbConnectionStringBuilder
            {
                ConnectionString = connectionSettings.ConnectionString
            };

            if (!builder.ContainsKey("Application Intent") && !builder.ContainsKey("ApplicationIntent"))
            {
                builder["Application Intent"] = "ReadOnly";
            }

            return builder.ConnectionString;
        }

        return connectionSettings.ConnectionString;
    }
}
