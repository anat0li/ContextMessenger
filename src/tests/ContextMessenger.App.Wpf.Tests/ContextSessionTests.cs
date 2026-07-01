using ContextMessenger.App.Wpf.Services;
using ContextMessenger.App.Wpf.Settings;
using ContextMessenger.Core.Meta;
using ContextMessenger.Protocol;

namespace ContextMessenger.App.Wpf.Tests;

public sealed class ContextSessionTests
{
    [Fact]
    public void GetCurrentContext_returns_target_root_and_description()
    {
        var target = new TargetProfile { Name = "ChatGPT", ProcessName = "ChatGPT.exe", Description = "Desktop client" };
        var root = new RootProfile { Name = "Repo", Path = "C:/repo", Description = "Main repository" };
        var roots = new FakeRootsProvider(target.Name, root);
        var coordinator = new FakeCoordinator();

        var session = new ContextSession(target, root, roots, coordinator);
        var ctx = session.GetCurrentContext();

        Assert.Equal("Repo", ctx.RootProfile.Name);
        Assert.Equal("C:/repo", ctx.RootProfile.Path);
        Assert.Equal("Main repository", ctx.RootProfile.Description);
        Assert.True(ctx.RootProfile.IsCurrent);
        Assert.Equal("ChatGPT", ctx.Target.Name);
        Assert.Equal("ChatGPT.exe", ctx.Target.Process);
        Assert.Equal("Desktop client", ctx.Target.Description);
        Assert.True(ctx.Target.IsCurrent);
        Assert.Equal("ContextMessenger", ctx.Server.Name);
        Assert.NotEmpty(ctx.Server.Version);
        Assert.NotEmpty(ctx.Protocol.Supported);
    }

    [Fact]
    public void ListRoots_marks_active_root_as_current_case_insensitive()
    {
        var target = new TargetProfile { Name = "T", ProcessName = "t" };
        var current = new RootProfile { Name = "MAIN", Path = "C:/main" };
        var other = new RootProfile { Name = "Other", Path = "C:/other" };
        var roots = new FakeRootsProvider("T", current, other);
        var session = new ContextSession(target, current, roots, new FakeCoordinator());

        var list = session.ListRoots();

        Assert.Equal(2, list.Count);
        Assert.True(list[0].IsCurrent);
        Assert.False(list[1].IsCurrent);
    }

    [Fact]
    public void Sql_root_metadata_omits_path_and_reports_kind()
    {
        var target = new TargetProfile { Name = "T", ProcessName = "t" };
        var root = new RootProfile
        {
            Name = "Database",
            Kind = RootKind.Sql,
            Sql = new SqlRootSettings { ReadOnly = true },
        };
        var session = new ContextSession(
            target,
            root,
            new FakeRootsProvider(target.Name, root),
            new FakeCoordinator());

        var context = session.GetCurrentContext();

        Assert.Null(context.RootProfile.Path);
        Assert.Equal(RootKind.Sql, context.RootProfile.Kind);
        Assert.True(context.RootProfile.ReadOnly);
    }

    [Fact]
    public void SetRoot_returns_new_context_but_defers_coordinator_until_apply()
    {
        var target = new TargetProfile { Name = "T", ProcessName = "t" };
        var main = new RootProfile { Name = "Main", Path = "C:/main" };
        var other = new RootProfile { Name = "Other", Path = "C:/other" };
        var roots = new FakeRootsProvider("T", main, other);
        var coordinator = new FakeCoordinator();
        var session = new ContextSession(target, main, roots, coordinator);

        var ctx = session.SetRoot("Other");

        Assert.Equal("Other", ctx.RootProfile.Name);
        Assert.True(ctx.RootProfile.IsCurrent);
        Assert.Empty(coordinator.Calls);

        var updated = session.GetCurrentContext();
        Assert.Equal("Other", updated.RootProfile.Name);

        session.ApplyPendingRootSwitch();

        Assert.Single(coordinator.Calls);
        Assert.Equal(("T", "Other"), coordinator.Calls[0]);
    }

    [Fact]
    public void Multiple_SetRoot_in_one_request_only_applies_the_last()
    {
        var target = new TargetProfile { Name = "T", ProcessName = "t" };
        var main = new RootProfile { Name = "Main", Path = "C:/main" };
        var other = new RootProfile { Name = "Other", Path = "C:/other" };
        var third = new RootProfile { Name = "Third", Path = "C:/third" };
        var roots = new FakeRootsProvider("T", main, other, third);
        var coordinator = new FakeCoordinator();
        var session = new ContextSession(target, main, roots, coordinator);

        session.SetRoot("Other");
        session.SetRoot("Third");
        session.ApplyPendingRootSwitch();

        Assert.Single(coordinator.Calls);
        Assert.Equal(("T", "Third"), coordinator.Calls[0]);
    }

    [Fact]
    public void ApplyPendingRootSwitch_is_a_noop_when_nothing_pending()
    {
        var target = new TargetProfile { Name = "T", ProcessName = "t" };
        var main = new RootProfile { Name = "Main", Path = "C:/main" };
        var coordinator = new FakeCoordinator();
        var session = new ContextSession(target, main, new FakeRootsProvider("T", main), coordinator);

        session.ApplyPendingRootSwitch();

        Assert.Empty(coordinator.Calls);
    }

    [Fact]
    public void ApplyPendingRootSwitch_clears_pending_so_it_only_fires_once()
    {
        var target = new TargetProfile { Name = "T", ProcessName = "t" };
        var main = new RootProfile { Name = "Main", Path = "C:/main" };
        var other = new RootProfile { Name = "Other", Path = "C:/other" };
        var coordinator = new FakeCoordinator();
        var session = new ContextSession(target, main, new FakeRootsProvider("T", main, other), coordinator);

        session.SetRoot("Other");
        session.ApplyPendingRootSwitch();
        session.ApplyPendingRootSwitch();

        Assert.Single(coordinator.Calls);
    }

    [Fact]
    public void SetRoot_matches_root_name_case_insensitively()
    {
        var target = new TargetProfile { Name = "T", ProcessName = "t" };
        var main = new RootProfile { Name = "Main", Path = "C:/main" };
        var other = new RootProfile { Name = "Other", Path = "C:/other" };
        var roots = new FakeRootsProvider("T", main, other);
        var coordinator = new FakeCoordinator();
        var session = new ContextSession(target, main, roots, coordinator);

        var ctx = session.SetRoot("other");
        session.ApplyPendingRootSwitch();

        Assert.Equal("Other", ctx.RootProfile.Name);
        Assert.Equal(("T", "Other"), coordinator.Calls[0]);
    }

    [Fact]
    public void SetRoot_throws_protocol_exception_for_unknown_root()
    {
        var target = new TargetProfile { Name = "T", ProcessName = "t" };
        var main = new RootProfile { Name = "Main", Path = "C:/main" };
        var roots = new FakeRootsProvider("T", main);
        var coordinator = new FakeCoordinator();
        var session = new ContextSession(target, main, roots, coordinator);

        var ex = Assert.Throws<ProtocolException>(() => session.SetRoot("Missing"));

        Assert.Equal(ProtocolErrorCodes.InvalidParameters, ex.Code);
        Assert.Empty(coordinator.Calls);
    }

    [Fact]
    public void SetRoot_throws_protocol_exception_when_name_is_empty()
    {
        var target = new TargetProfile { Name = "T", ProcessName = "t" };
        var main = new RootProfile { Name = "Main", Path = "C:/main" };
        var session = new ContextSession(target, main, new FakeRootsProvider("T", main), new FakeCoordinator());

        var ex = Assert.Throws<ProtocolException>(() => session.SetRoot(""));

        Assert.Equal(ProtocolErrorCodes.InvalidParameters, ex.Code);
    }

    private sealed class FakeRootsProvider : IAvailableProfilesProvider
    {
        private readonly RootProfile[] _roots;
        private readonly TargetProfile[] _targets;

        public FakeRootsProvider(string targetName, params RootProfile[] roots)
        {
            _roots = roots;
            _targets = [new TargetProfile { Name = targetName, ProcessName = targetName }];
        }

        public IReadOnlyList<RootProfile> GetAvailableRoots() => _roots;

        public IReadOnlyList<TargetProfile> GetAvailableTargets() => _targets;
    }

    private sealed class FakeCoordinator : IRootSwitchCoordinator
    {
        public List<(string Target, string Root)> Calls { get; } = new();

        public void ActivateRootForTarget(string targetName, string rootName) =>
            Calls.Add((targetName, rootName));
    }
}
