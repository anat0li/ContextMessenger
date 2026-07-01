using ContextMessenger.App.Wpf.Settings;

namespace ContextMessenger.App.Wpf.Services;

public sealed class SettingsAvailableProfilesProvider : IAvailableProfilesProvider
{
    private readonly ISettingsStore _settings;

    public SettingsAvailableProfilesProvider(ISettingsStore settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public IReadOnlyList<RootProfile> GetAvailableRoots() => _settings.Load().Roots;

    public IReadOnlyList<TargetProfile> GetAvailableTargets() => _settings.Load().Targets;
}
