using ContextMessenger.App.Wpf.Settings;

namespace ContextMessenger.App.Wpf.Services;

public interface IAvailableProfilesProvider
{
    IReadOnlyList<RootProfile> GetAvailableRoots();

    IReadOnlyList<TargetProfile> GetAvailableTargets();
}
