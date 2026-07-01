namespace ContextMessenger.Data;

public sealed class DataRootSession(
    IDataConnectionFactory connectionFactory,
    IDataSchemaReader schemaReader,
    IDataQueryService queryService,
    DataProviderSettings providerSettings,
    DataConnectionSettings connectionSettings) : IDataRootSession
{
    public DataSchemaInfo ReadSchema(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = connectionFactory.OpenConnection(providerSettings, connectionSettings);
        cancellationToken.ThrowIfCancellationRequested();
        var schema = schemaReader.ReadSchema(connection);
        cancellationToken.ThrowIfCancellationRequested();
        return schema;
    }

    public DataQueryResult ExecuteQuery(
        string sql,
        DataQueryPageRequest? page = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = connectionFactory.OpenConnection(providerSettings, connectionSettings);
        return queryService.Execute(connection, sql, connectionSettings, page, cancellationToken);
    }
}
