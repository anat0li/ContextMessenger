using ContextMessenger.App.Wpf.Settings;

namespace ContextMessenger.App.Wpf.Services;

public interface ISessionFactory
{
    LoopSession Create(TargetProfile target, RootProfile root);
}
