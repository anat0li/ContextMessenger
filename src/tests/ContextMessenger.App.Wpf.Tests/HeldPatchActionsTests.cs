using System.Threading;
using System.Threading.Tasks;
using ContextMessenger.App.Wpf.Patching;
using ContextMessenger.App.Wpf.Services;
using ContextMessenger.Core.Patching;
using ContextMessenger.Protocol.Review;

namespace ContextMessenger.App.Wpf.Tests;

public sealed class HeldPatchActionsTests
{
    private static HeldPatchInteraction Held(string status = PatchTransactionStatuses.AwaitingAcceptance) => new()
    {
        RootName = "Repo",
        TargetName = "ChatGPT",
        PatchId = "p-1",
        RequestId = "req-7",
        CommandType = "propose_patch",
        Revision = 1,
        TransactionStatus = status,
        HeldResponseText = "BEGIN_RESPONSE\n{}\nEND_RESPONSE",
    };

    private static PatchTransactionResult Result(string status) =>
        new() { PatchStatus = status, PatchId = "p-1", Revision = 1 };

    private static readonly IPatchDiffService Diff = new FakeDiff();

    [Fact]
    public async Task Accept_stages_via_service_submits_correlated_response_and_clears()
    {
        var store = new InMemoryHeldPatchInteractionStore();
        store.Save(Held());
        var patches = new FakePatches { AcceptResult = Result("accepted") };
        var automation = new FakeAutomation();
        var changed = 0;
        var actions = new HeldPatchActions(patches, automation, store, Diff, ".", () => changed++);

        await actions.AcceptAsync();

        Assert.Equal("p-1", patches.LastAcceptId);
        var submitted = Assert.Single(automation.Submitted);
        Assert.Contains("accepted", submitted);
        Assert.Contains("\"id\": \"req-7\"", submitted);
        Assert.Null(store.Current);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task Revert_reverts_via_service_submits_and_clears()
    {
        var store = new InMemoryHeldPatchInteractionStore();
        store.Save(Held(PatchTransactionStatuses.NeedsRevision));
        var patches = new FakePatches { RevertResult = Result("reverted") };
        var automation = new FakeAutomation();
        var actions = new HeldPatchActions(patches, automation, store, Diff, ".", () => { });

        await actions.RevertAsync();

        Assert.Equal("p-1", patches.LastRevertId);
        Assert.Contains("reverted", automation.Submitted[0]);
        Assert.Null(store.Current);
    }

    [Fact]
    public async Task Send_submits_held_text_and_advances_phase()
    {
        var store = new InMemoryHeldPatchInteractionStore();
        store.Save(Held());
        var automation = new FakeAutomation();
        var actions = new HeldPatchActions(new FakePatches(), automation, store, Diff, ".", () => { });

        await actions.SendAsync([]);

        Assert.Contains("BEGIN_RESPONSE", automation.Submitted[0]);
        Assert.NotNull(store.Current);
        Assert.Equal(PatchInteractionPhase.AwaitingModelReply, store.Current!.Phase);
    }

    [Fact]
    public async Task Send_injects_reviewer_comments_into_the_response()
    {
        var store = new InMemoryHeldPatchInteractionStore();
        store.Save(Held());
        var automation = new FakeAutomation();
        var actions = new HeldPatchActions(new FakePatches(), automation, store, Diff, ".", () => { });

        await actions.SendAsync([new ReviewerComment { Id = "c-1", Path = "src/A.cs", Line = 3, Comment = "why?" }]);

        Assert.Contains("reviewerComments", automation.Submitted[0]);
        Assert.Contains("c-1", automation.Submitted[0]);
    }

    [Fact]
    public async Task Review_actions_publish_submitted_responses_for_logging()
    {
        var store = new InMemoryHeldPatchInteractionStore();
        store.Save(Held());
        var automation = new FakeAutomation();
        var logged = new List<string>();
        var actions = new HeldPatchActions(new FakePatches(), automation, store, Diff, ".", () => { }, logged.Add);

        await actions.SendAsync([new ReviewerComment { Id = "c-1", Path = "src/A.cs", Line = 3, Comment = "why?" }]);

        var response = Assert.Single(logged);
        Assert.Equal(automation.Submitted[0], response);
        Assert.Contains("reviewerComments", response);
    }

    [Fact]
    public async Task Review_actions_do_not_publish_failed_submissions_for_logging()
    {
        var store = new InMemoryHeldPatchInteractionStore();
        store.Save(Held());
        var automation = new FakeAutomation { SubmitResult = false };
        var logged = new List<string>();
        var actions = new HeldPatchActions(new FakePatches(), automation, store, Diff, ".", () => { }, logged.Add);

        await actions.SendAsync([new ReviewerComment { Id = "c-1", Path = "src/A.cs", Line = 3, Comment = "why?" }]);

        Assert.Empty(logged);
        Assert.Single(automation.Submitted);
    }

    [Fact]
    public async Task Refresh_clears_when_patch_gone()
    {
        var store = new InMemoryHeldPatchInteractionStore();
        store.Save(Held());
        var patches = new FakePatches { CurrentResult = new PatchTransactionResult { PatchStatus = "none" } };
        var actions = new HeldPatchActions(patches, new FakeAutomation(), store, Diff, ".", () => { });

        await actions.RefreshAsync();

        Assert.Null(store.Current);
    }

    [Fact]
    public async Task Refresh_updates_status_when_patch_still_active()
    {
        var store = new InMemoryHeldPatchInteractionStore();
        store.Save(Held(PatchTransactionStatuses.AwaitingAcceptance));
        var patches = new FakePatches { CurrentResult = Result("needs_revision") with { Revision = 3 } };
        var actions = new HeldPatchActions(patches, new FakeAutomation(), store, Diff, ".", () => { });

        await actions.RefreshAsync();

        Assert.NotNull(store.Current);
        Assert.Equal("needs_revision", store.Current!.TransactionStatus);
        Assert.Equal(3, store.Current.Revision);
    }

    [Fact]
    public async Task No_interaction_is_a_noop()
    {
        var store = new InMemoryHeldPatchInteractionStore();
        var automation = new FakeAutomation();
        var actions = new HeldPatchActions(new FakePatches(), automation, store, Diff, ".", () => Assert.Fail("should not notify"));

        await actions.AcceptAsync();
        await actions.RevertAsync();
        await actions.SendAsync([]);
        await actions.RefreshAsync();

        Assert.Empty(automation.Submitted);
    }

    private sealed class FakeDiff : IPatchDiffService
    {
        public string? GetUnifiedDiff(string relativePath) => null;
    }

    private sealed class FakeAutomation : ITargetAutomationAdapter
    {
        public List<string> Submitted { get; } = [];

        public bool SubmitResult { get; set; } = true;

        public Task<bool> SubmitResponseAsync(string text, CancellationToken cancellationToken)
        {
            Submitted.Add(text);
            return Task.FromResult(SubmitResult);
        }
    }

    private sealed class FakePatches : IPatchTransactionService
    {
        public PatchTransactionResult AcceptResult { get; set; } = new() { PatchStatus = "accepted", PatchId = "p-1", Revision = 1 };
        public PatchTransactionResult RevertResult { get; set; } = new() { PatchStatus = "reverted", PatchId = "p-1", Revision = 1 };
        public PatchTransactionResult CurrentResult { get; set; } = new() { PatchStatus = "none" };
        public string? LastAcceptId { get; private set; }
        public string? LastRevertId { get; private set; }

        public bool HasActivePatch => false;
        public bool DeferAcceptanceByDefault { get; set; }

        public PatchTransactionResult Accept(string patchId) { LastAcceptId = patchId; return AcceptResult; }
        public PatchTransactionResult Revert(string patchId) { LastRevertId = patchId; return RevertResult; }
        public PatchTransactionResult Current() => CurrentResult;
        public PatchTransactionResult Propose(ProposePatchRequest request) => throw new NotSupportedException();
        public PatchTransactionResult Amend(AmendPatchRequest request) => throw new NotSupportedException();
        public PatchValidationResult Validate(ValidatePatchRequest request) => throw new NotSupportedException();
    }
}
