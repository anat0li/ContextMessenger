using System.Data.Common;

namespace ContextMessenger.Data;

public interface IDataConnectionFactory
{
    DbConnection OpenConnection(DataProviderSettings providerSettings, DataConnectionSettings connectionSettings);
}
