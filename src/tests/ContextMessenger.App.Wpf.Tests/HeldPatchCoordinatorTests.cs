using ContextMessenger.App.Wpf.Patching;
using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.App.Wpf.Tests;

public sealed class HeldPatchCoordinatorTests
{
    private static PatchHoldRequest Request(
        string status,
        bool holdEnabled,
        string commandType = "propose_patch",
        string patchId = "p-1",
        int revision = 1,
        string responseText = "BEGIN_RESPONSE\n{}\nEND_RESPONSE") => new()
    {
        RootName = "Repo",
        TargetName = "ChatGPT",
        ResponseText = responseText,
        HoldEnabled = holdEnabled,
        Outcome = new PatchOutcome
        {
            CommandType = commandType,
            PatchStatus = status,
            PatchId = patchId,
            Revision = revision,
        },
    };

    [Fact]
    public void Accepted_outcome_delivers_and_clears_existing()
    {
        var store = new InMemoryHeldPatchInteractionStore();
        var coordinator = new HeldPatchCoordinator(store);
        // Seed a held interaction first.
        coordinator.Evaluate(Request(PatchTransactionStatuses.AwaitingAcceptance, holdEnabled: true));
        Assert.NotNull(store.Current);

        var decision = coordinator.Evaluate(Request("accepted", holdEnabled: true));

        Assert.Equal(PatchHoldDecision.Deliver, decision);
        Assert.Null(store.Current);
    }

    [Fact]
    public void Accepted_with_no_existing_interaction_delivers()
    {
        var store = new InMemoryHeldPatchInteractionStore();
        var coordinator = new HeldPatchCoordinator(store);

        var decision = coordinator.Evaluate(Request("accepted", holdEnabled: true));

        Assert.Equal(PatchHoldDecision.Deliver, decision);
        Assert.Null(store.Current);
    }

    [Fact]
    public void Reverted_outcome_delivers_and_clears_existing()
    {
        var store = new InMemoryHeldPatchInteractionStore();
        var coordinator = new HeldPatchCoordinator(store);
        // Seed a held interaction first.
        coordinator.Evaluate(Request(PatchTransactionStatuses.AwaitingAcceptance, holdEnabled: true));
        Assert.NotNull(store.Current);

        var decision = coordinator.Evaluate(Request(PatchTransactionStatuses.Reverted, holdEnabled: true));

        // Revert disposes the patch: terminal outcome closes the review page and delivers.
        Assert.Equal(PatchHoldDecision.Deliver, decision);
        Assert.Null(store.Current);
    }

    [Fact]
    public void Hold_disabled_delivers_but_still_creates_review_page()
    {
        var store = new InMemoryHeldPatchInteractionStore();
        var coordinator = new HeldPatchCoordinator(store);

        var decision = coordinator.Evaluate(Request(PatchTransactionStatuses.NeedsRevision, holdEnabled: false, responseText: "RESP"));

        // Page-creation rule: a proposed patch that is not immediately accepted/reverted opens a
        // review page even when hold is off; the response is still delivered to the model, so the
        // host is now awaiting the model's reply.
        Assert.Equal(PatchHoldDecision.Deliver, decision);
        Assert.NotNull(store.Current);
        var held = store.Current!;
        Assert.Equal(PatchTransactionStatuses.NeedsRevision, held.TransactionStatus);
        Assert.Equal(PatchInteractionPhase.AwaitingModelReply, held.Phase);
        Assert.Equal("RESP", held.HeldResponseText);
    }

    [Theory]
    [InlineData(PatchTransactionStatuses.NeedsRevision)]
    [InlineData(PatchTransactionStatuses.AwaitingAcceptance)]
    public void Holdable_outcome_with_hold_enabled_holds_and_creates_interaction(string status)
    {
        var store = new InMemoryHeldPatchInteractionStore();
        var coordinator = new HeldPatchCoordinator(store);

        var decision = coordinator.Evaluate(Request(status, holdEnabled: true, responseText: "RESP"));

        Assert.Equal(PatchHoldDecision.Hold, decision);
        Assert.NotNull(store.Current);
        var held = store.Current!;
        Assert.Equal(status, held.TransactionStatus);
        Assert.Equal(PatchInteractionPhase.Reviewing, held.Phase);
        Assert.Equal("RESP", held.HeldResponseText);
        Assert.Equal("p-1", held.PatchId);
        var entry = Assert.Single(held.History);
        Assert.Equal(PatchInteractionDirection.Inbound, entry.Direction);
    }

    [Fact]
    public void Non_holdable_status_delivers_even_with_hold_enabled()
    {
        var store = new InMemoryHeldPatchInteractionStore();
        var coordinator = new HeldPatchCoordinator(store);

        var decision = coordinator.Evaluate(Request("none", holdEnabled: true));

        Assert.Equal(PatchHoldDecision.Deliver, decision);
        Assert.Null(store.Current);
    }

    [Fact]
    public void Same_patch_evolving_updates_interaction_and_appends_history()
    {
        var store = new InMemoryHeldPatchInteractionStore();
        var coordinator = new HeldPatchCoordinator(store);

        coordinator.Evaluate(Request(
            PatchTransactionStatuses.NeedsRevision, holdEnabled: true,
            commandType: "propose_patch", revision: 1, responseText: "R1"));

        var decision = coordinator.Evaluate(Request(
            PatchTransactionStatuses.AwaitingAcceptance, holdEnabled: true,
            commandType: "amend_patch", revision: 2, responseText: "R2"));

        Assert.Equal(PatchHoldDecision.Hold, decision);
        Assert.NotNull(store.Current);
        var held = store.Current!;
        Assert.Equal(PatchTransactionStatuses.AwaitingAcceptance, held.TransactionStatus);
        Assert.Equal(2, held.Revision);
        Assert.Equal("R2", held.HeldResponseText);
        Assert.Equal(2, held.History.Count);
        Assert.Equal(["propose_patch -> needs_revision", "amend_patch -> awaiting_acceptance"],
            held.History.Select(h => h.Summary));
    }

    [Fact]
    public void Build_errors_flow_from_outcome_to_interaction()
    {
        var store = new InMemoryHeldPatchInteractionStore();
        var coordinator = new HeldPatchCoordinator(store);
        var request = Request(PatchTransactionStatuses.NeedsRevision, holdEnabled: true);
        request = request with
        {
            Outcome = request.Outcome with
            {
                BuildErrors = [new PatchBuildError { Code = "CS1002", Path = "src/A.cs", Line = 3, Message = "; expected" }],
            },
        };

        coordinator.Evaluate(request);

        Assert.NotNull(store.Current);
        var error = Assert.Single(store.Current!.BuildErrors);
        Assert.Equal("CS1002", error.Code);
        Assert.Equal("src/A.cs", error.Path);
    }

    [Fact]
    public void Build_warnings_and_summaries_flow_from_outcome_to_interaction()
    {
        var store = new InMemoryHeldPatchInteractionStore();
        var coordinator = new HeldPatchCoordinator(store);
        var request = Request(PatchTransactionStatuses.AwaitingAcceptance, holdEnabled: true);
        request = request with
        {
            Outcome = request.Outcome with
            {
                BuildWarnings = [new PatchBuildWarning { Code = "CS0168", Path = "src/A.cs", Line = 3, Message = "unused" }],
                BuildSummary = new PatchStageSummary { Status = "passed", Policy = "solution", DurationMs = 100 },
                TestSummary = new PatchStageSummary { Status = "passed", TotalTests = 2, FailedTests = 0 },
            },
        };

        coordinator.Evaluate(request);

        Assert.NotNull(store.Current);
        var warning = Assert.Single(store.Current!.BuildWarnings);
        Assert.Equal("CS0168", warning.Code);
        Assert.Equal("passed", store.Current.BuildSummary.Status);
        Assert.Equal("solution", store.Current.BuildSummary.Policy);
        Assert.Equal("passed", store.Current.TestSummary.Status);
        Assert.Equal(2, store.Current.TestSummary.TotalTests);
    }

    [Fact]
    public void Test_failures_flow_from_outcome_to_interaction()
    {
        var store = new InMemoryHeldPatchInteractionStore();
        var coordinator = new HeldPatchCoordinator(store);
        var request = Request(PatchTransactionStatuses.NeedsRevision, holdEnabled: true);
        request = request with
        {
            Outcome = request.Outcome with
            {
                TestFailures = [new PatchTestFailure { Code = "T.Fails", Path = "src/T.cs", Line = 9, Message = "boom" }],
            },
        };

        coordinator.Evaluate(request);

        Assert.NotNull(store.Current);
        var failure = Assert.Single(store.Current!.TestFailures);
        Assert.Equal("T.Fails", failure.Code);
    }

    [Fact]
    public void Comment_replies_flow_from_outcome_to_interaction()
    {
        var store = new InMemoryHeldPatchInteractionStore();
        var coordinator = new HeldPatchCoordinator(store);
        var request = Request(PatchTransactionStatuses.NeedsRevision, holdEnabled: true);
        request = request with
        {
            Outcome = request.Outcome with
            {
                CommentReplies = [new PatchCommentReply { Id = "c-1", Reply = "done" }],
            },
        };

        coordinator.Evaluate(request);

        Assert.NotNull(store.Current);
        var reply = Assert.Single(store.Current!.CommentReplies);
        Assert.Equal("c-1", reply.Id);
        Assert.Equal("done", reply.Reply);
    }

    [Fact]
    public void Reply_turn_increments_across_an_evolving_patch()
    {
        var store = new InMemoryHeldPatchInteractionStore();
        var coordinator = new HeldPatchCoordinator(store);

        coordinator.Evaluate(Request(PatchTransactionStatuses.NeedsRevision, holdEnabled: true));
        Assert.Equal(0, store.Current!.ReplyTurn); // first hold

        coordinator.Evaluate(Request(PatchTransactionStatuses.AwaitingAcceptance, holdEnabled: true));
        Assert.Equal(1, store.Current!.ReplyTurn); // same patch evolving (e.g. a reply amend)
    }

    [Fact]
    public void Different_patch_id_replaces_interaction()
    {
        var store = new InMemoryHeldPatchInteractionStore();
        var coordinator = new HeldPatchCoordinator(store);

        coordinator.Evaluate(Request(PatchTransactionStatuses.NeedsRevision, holdEnabled: true, patchId: "p-1"));
        coordinator.Evaluate(Request(PatchTransactionStatuses.AwaitingAcceptance, holdEnabled: true, patchId: "p-2"));

        Assert.NotNull(store.Current);
        var held = store.Current!;
        Assert.Equal("p-2", held.PatchId);
        Assert.Single(held.History); // fresh interaction, not appended
    }
}
