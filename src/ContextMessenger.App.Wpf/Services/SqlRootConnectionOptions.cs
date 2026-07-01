using ContextMessenger.App.Wpf.Settings;
using ContextMessenger.Core.Meta;
using ContextMessenger.Data;

namespace ContextMessenger.App.Wpf.Services;

internal static class SqlRootConnectionOptions
{
    public static DataProviderSettings CreateProviderSettings(RootProfile root)
    {
        var sql = GetReadOnlySqlSettings(root);
        return new DataProviderSettings
        {
            ProviderInvariantName = sql.ProviderInvariantName,
            ProviderAssemblyPath = sql.ProviderAssemblyPath,
            ProviderFactoryTypeName = sql.ProviderFactoryTypeName,
        };
    }

    public static DataConnectionSettings CreateConnectionSettings(
        RootProfile root,
        ISqlConnectionStringResolver connectionStringResolver)
    {
        ArgumentNullException.ThrowIfNull(connectionStringResolver);

        var sql = GetReadOnlySqlSettings(root);
        return new DataConnectionSettings
        {
            ConnectionString = connectionStringResolver.Resolve(sql),
            ReadOnly = true,
            CommandTimeoutSeconds = sql.CommandTimeoutSeconds,
            MaxRows = sql.MaxRows,
            MaxCellBytes = sql.MaxCellBytes,
        };
    }

    private static SqlRootSettings GetReadOnlySqlSettings(RootProfile root)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (root.Kind != RootKind.Sql)
            throw new InvalidOperationException($"Root '{root.Name}' is not a SQL root.");

        var sql = root.Sql
            ?? throw new InvalidOperationException($"SQL root '{root.Name}' has no SQL settings.");

        if (!sql.ReadOnly)
            throw new InvalidOperationException($"SQL root '{root.Name}' must be configured read-only.");

        return sql;
    }
}
