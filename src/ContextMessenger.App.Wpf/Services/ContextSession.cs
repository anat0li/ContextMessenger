using System.Reflection;
using ContextMessenger.App.Wpf.Settings;
using ContextMessenger.Core.Meta;
using ContextMessenger.Protocol;

namespace ContextMessenger.App.Wpf.Services;

public sealed class ContextSession : IContextSession
{
    private static readonly string AssemblyVersion = ResolveAssemblyVersion();

    private readonly TargetProfile _target;
    private readonly IAvailableProfilesProvider _profilesProvider;
    private readonly IRootSwitchCoordinator _coordinator;
    private RootProfile _root;
    private string? _pendingRootName;

    public ContextSession(
        TargetProfile target,
        RootProfile root,
        IAvailableProfilesProvider profilesProvider,
        IRootSwitchCoordinator coordinator)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _profilesProvider = profilesProvider ?? throw new ArgumentNullException(nameof(profilesProvider));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    public CurrentContextInfo GetCurrentContext() => BuildContext(_root);

    public IReadOnlyList<RootProfileInfo> ListRoots()
    {
        var roots = _profilesProvider.GetAvailableRoots();
        return roots
            .Select(r => new RootProfileInfo
            {
                Name = r.Name,
                Path = r.Kind == RootKind.FileSystem ? r.Path : null,
                Description = r.Description,
                Kind = r.Kind,
                ReadOnly = r.Kind == RootKind.Sql && r.Sql?.ReadOnly != false,
                IsCurrent = IsSameName(r.Name, _root.Name),
            })
            .ToArray();
    }

    public IReadOnlyList<TargetProfileInfo> ListTargets()
    {
        var targets = _profilesProvider.GetAvailableTargets();
        return targets
            .Select(t => new TargetProfileInfo
            {
                Name = t.Name,
                Process = t.ProcessName,
                Description = t.Description,
                IsCurrent = IsSameName(t.Name, _target.Name),
            })
            .ToArray();
    }

    public CurrentContextInfo SetRoot(string name)
    {
        if (string.IsNullOrEmpty(name))
            throw new ProtocolException(ProtocolErrorCodes.InvalidParameters, "Root name is required.");

        var available = _profilesProvider.GetAvailableRoots();
        var match = available.FirstOrDefault(r => IsSameName(r.Name, name))
            ?? throw new ProtocolException(
                ProtocolErrorCodes.InvalidParameters,
                $"Root '{name}' is not in the available roots list.");

        _root = match;
        _pendingRootName = match.Name;
        return BuildContext(match);
    }

    public void ApplyPendingRootSwitch()
    {
        var pending = _pendingRootName;
        if (pending is null)
            return;

        _pendingRootName = null;
        _coordinator.ActivateRootForTarget(_target.Name, pending);
    }

    private CurrentContextInfo BuildContext(RootProfile root) => new()
    {
        RootProfile = new RootProfileInfo
        {
            Name = root.Name,
            Path = root.Kind == RootKind.FileSystem ? root.Path : null,
            Description = root.Description,
            Kind = root.Kind,
            ReadOnly = root.Kind == RootKind.Sql && root.Sql?.ReadOnly != false,
            IsCurrent = true,
        },
        Target = new TargetProfileInfo
        {
            Name = _target.Name,
            Process = _target.ProcessName,
            Description = _target.Description,
            IsCurrent = true,
        },
        Server = new ServerInfo
        {
            Name = "ContextMessenger",
            Version = AssemblyVersion,
        },
        Protocol = new ProtocolInfo
        {
            Supported = [$"{ProtocolValidator.CurrentVersion.Major}.{ProtocolValidator.CurrentVersion.Minor}"],
        },
    };

    private static bool IsSameName(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string ResolveAssemblyVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(info))
            return info;
        return asm.GetName().Version?.ToString() ?? "";
    }
}
