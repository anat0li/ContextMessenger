namespace ContextMessenger.Data;

public interface IDataRootSession
{
    DataSchemaInfo ReadSchema(CancellationToken cancellationToken = default);

    DataQueryResult ExecuteQuery(
        string sql,
        DataQueryPageRequest? page = null,
        CancellationToken cancellationToken = default);
}
