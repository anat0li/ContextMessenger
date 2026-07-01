using ContextMessenger.App.Wpf.Settings;

namespace ContextMessenger.App.Wpf.Services;

public interface ISqlConnectionTester
{
    void Test(RootProfile root);
}
