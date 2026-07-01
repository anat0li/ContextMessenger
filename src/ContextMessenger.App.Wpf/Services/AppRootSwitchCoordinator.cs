using ContextMessenger.Core.Meta;

namespace ContextMessenger.App.Wpf.Services;

public sealed class AppRootSwitchCoordinator : IRootSwitchCoordinator
{
    public event Action<string, string>? RootSwitchRequested;

    public void ActivateRootForTarget(string targetName, string rootName)
    {
        if (string.IsNullOrEmpty(targetName))
            throw new ArgumentException("Target name is required.", nameof(targetName));
        if (string.IsNullOrEmpty(rootName))
            throw new ArgumentException("Root name is required.", nameof(rootName));

        RootSwitchRequested?.Invoke(targetName, rootName);
    }
}
