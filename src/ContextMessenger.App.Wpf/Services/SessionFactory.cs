using ContextMessenger.App.Wpf.Settings;
using ContextMessenger.Core.Meta;
using ContextMessenger.Core.Patching;
using ContextMessenger.Data;
using ContextMessenger.FileSystem;
using ContextMessenger.Patching;
using ContextMessenger.Protocol.Dispatch;
using ContextMessenger.Roslyn;

namespace ContextMessenger.App.Wpf.Services;

public sealed class SessionFactory : ISessionFactory
{
    private readonly IAvailableProfilesProvider _profilesProvider;
    private readonly IRootSwitchCoordinator _coordinator;
    private readonly IPatchSessionStore? _patchSessionStore;
    private readonly ISqlConnectionStringResolver _connectionStringResolver;

    public SessionFactory(
        IAvailableProfilesProvider profilesProvider,
        IRootSwitchCoordinator coordinator,
        IPatchSessionStore? patchSessionStore = null,
        ISqlConnectionStringResolver? connectionStringResolver = null)
    {
        _profilesProvider = profilesProvider ?? throw new ArgumentNullException(nameof(profilesProvider));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _patchSessionStore = patchSessionStore;
        _connectionStringResolver = connectionStringResolver ?? new SqlConnectionStringResolver();
    }

    public LoopSession Create(TargetProfile target, RootProfile root)
    {
        var session = new ContextSession(target, root, _profilesProvider, _coordinator);
        if (root.Kind == RootKind.Sql)
            return CreateSqlSession(root, session);

        var fs = new FileSystemContextService(root.Path);
        var roslyn = new DocumentSymbolService(root.Path);
        var gitStatus = new LibGit2SharpGitStatusService(root.Path);
        var patchTransactions = new PatchTransactionService(
            root.Path,
            root.Name,
            _patchSessionStore,
            workspaceInvalidator: roslyn,
            roslynNavigation: roslyn,
            deferAcceptanceByDefault: root.HoldPatchResponsesForReview);
        var dispatcher = CommandDispatcher.ForServices(fs, roslyn, session, gitStatus, patchTransactions);
        var processor = new DispatcherRequestProcessor(dispatcher, session);
        return new LoopSession(processor, patchTransactions);
    }

    private LoopSession CreateSqlSession(RootProfile root, IContextSession session)
    {
        var sql = root.Sql
            ?? throw new InvalidOperationException($"SQL root '{root.Name}' has no SQL settings.");
        if (!sql.ReadOnly)
            throw new InvalidOperationException($"SQL root '{root.Name}' must be configured read-only.");

        var providerSettings = SqlRootConnectionOptions.CreateProviderSettings(root);
        var connectionSettings = SqlRootConnectionOptions.CreateConnectionSettings(root, _connectionStringResolver);
        var dataSession = new DataRootSession(
            new DataConnectionFactory(new ReflectionDataProviderResolver()),
            new DataSchemaReader(),
            new DataQueryService(new ReadOnlySqlGuard()),
            providerSettings,
            connectionSettings);
        var dispatcher = CommandDispatcher.ForServices(
            fs: null,
            roslyn: null,
            session: session,
            gitStatus: null,
            patchTransactions: null,
            dataRootSession: dataSession,
            allowSchemaCommands: sql.AllowSchemaCommands);
        return new LoopSession(new DispatcherRequestProcessor(dispatcher, session), Patches: null);
    }

    private sealed class DispatcherRequestProcessor : IRequestProcessor
    {
        private readonly CommandDispatcher _dispatcher;
        private readonly IContextSession _session;

        public DispatcherRequestProcessor(CommandDispatcher dispatcher, IContextSession session)
        {
            _dispatcher = dispatcher;
            _session = session;
        }

        public ProcessRequestsResult ProcessRequestBodies(
            IReadOnlyList<string> requests,
            CancellationToken cancellationToken = default) =>
            _dispatcher.ProcessRequestsDetailed(requests, cancellationToken);

        public void OnResponseSubmitted() => _session.ApplyPendingRootSwitch();
    }
}
