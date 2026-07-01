using System.Data.Common;

namespace ContextMessenger.Data;

public interface IDataSchemaReader
{
    DataSchemaInfo ReadSchema(DbConnection connection);
}
