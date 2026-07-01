using ContextMessenger.App.Wpf.Settings;

namespace ContextMessenger.App.Wpf.Services;

public interface ISqlConnectionStringResolver
{
    string Resolve(SqlRootSettings settings);
}
