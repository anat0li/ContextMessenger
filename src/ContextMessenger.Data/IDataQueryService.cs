using System.Data.Common;

namespace ContextMessenger.Data;

public interface IDataQueryService
{
    DataQueryResult Execute(
        DbConnection connection,
        string sql,
        DataConnectionSettings settings,
        DataQueryPageRequest? page = null,
        CancellationToken cancellationToken = default);
}
