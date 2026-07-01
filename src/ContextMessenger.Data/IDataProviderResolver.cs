using System.Data.Common;

namespace ContextMessenger.Data;

public interface IDataProviderResolver
{
    DbProviderFactory Resolve(DataProviderSettings settings);
}
