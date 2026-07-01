using ContextMessenger.Core.Patching;

namespace ContextMessenger.App.Wpf.Services;

public sealed class SettingsPatchSessionStore : IPatchSessionStore
{
    private readonly ISettingsStore _settings;

    public SettingsPatchSessionStore(ISettingsStore settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public PatchSessionMetadata? Load() => _settings.Load().ActivePatch;

    public void Save(PatchSessionMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var current = _settings.Load();
        _settings.Save(current with { ActivePatch = metadata });
    }

    public void Clear()
    {
        var current = _settings.Load();
        if (current.ActivePatch is null)
            return;

        _settings.Save(current with { ActivePatch = null });
    }
}
