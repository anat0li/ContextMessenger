using ContextMessenger.App.Wpf.Patching;

namespace ContextMessenger.App.Wpf.Tests;

public sealed class HeldPatchInteractionTests
{
    private static HeldPatchInteraction Sample(string status = PatchTransactionStatuses.AwaitingAcceptance) => new()
    {
        RootName = "Repo",
        TargetName = "ChatGPT",
        PatchId = "p-1",
        Revision = 1,
        TransactionStatus = status,
        HeldResponseText = "BEGIN_RESPONSE\n{}\nEND_RESPONSE",
    };

    [Fact]
    public void New_interaction_defaults_to_reviewing_phase()
    {
        var held = Sample();
        Assert.Equal(PatchInteractionPhase.Reviewing, held.Phase);
        Assert.Empty(held.History);
    }

    [Fact]
    public void WithPhase_advances_phase_and_stamps_updated()
    {
        var held = Sample();
        var before = held.UpdatedAtUtc;

        var advanced = held.WithPhase(PatchInteractionPhase.AwaitingModelReply);

        Assert.Equal(PatchInteractionPhase.AwaitingModelReply, advanced.Phase);
        Assert.True(advanced.UpdatedAtUtc >= before);
        // Original is unchanged (record immutability).
        Assert.Equal(PatchInteractionPhase.Reviewing, held.Phase);
    }

    [Fact]
    public void WithStatus_changes_status_and_optional_revision()
    {
        var held = Sample(PatchTransactionStatuses.NeedsRevision);

        var reverted = held.WithStatus(PatchTransactionStatuses.Reverted);
        Assert.Equal(PatchTransactionStatuses.Reverted, reverted.TransactionStatus);
        Assert.Equal(1, reverted.Revision);

        var bumped = held.WithStatus(PatchTransactionStatuses.AwaitingAcceptance, revision: 2);
        Assert.Equal(PatchTransactionStatuses.AwaitingAcceptance, bumped.TransactionStatus);
        Assert.Equal(2, bumped.Revision);
    }

    [Fact]
    public void AppendHistory_adds_entry_without_mutating_original()
    {
        var held = Sample();
        var entry = new PatchInteractionEntry
        {
            Direction = PatchInteractionDirection.Inbound,
            Summary = "amend received",
            Revision = 2,
        };

        var updated = held.AppendHistory(entry);

        Assert.Empty(held.History);
        var only = Assert.Single(updated.History);
        Assert.Equal("amend received", only.Summary);
        Assert.Equal(PatchInteractionDirection.Inbound, only.Direction);
    }

    [Fact]
    public void AppendHistory_preserves_order()
    {
        var held = Sample()
            .AppendHistory(new PatchInteractionEntry { Direction = PatchInteractionDirection.Inbound, Summary = "first" })
            .AppendHistory(new PatchInteractionEntry { Direction = PatchInteractionDirection.Outbound, Summary = "second" });

        Assert.Equal(["first", "second"], held.History.Select(h => h.Summary));
    }

    [Theory]
    [InlineData(PatchTransactionStatuses.NeedsRevision, true)]
    [InlineData(PatchTransactionStatuses.AwaitingAcceptance, true)]
    [InlineData(PatchTransactionStatuses.Reverted, true)]
    [InlineData("accepted", false)]
    [InlineData("none", false)]
    [InlineData(null, false)]
    public void IsHoldable_recognizes_active_statuses(string? status, bool expected)
    {
        Assert.Equal(expected, PatchTransactionStatuses.IsHoldable(status));
    }
}

public sealed class InMemoryHeldPatchInteractionStoreTests
{
    private static HeldPatchInteraction Sample() => new()
    {
        RootName = "Repo",
        TargetName = "ChatGPT",
        PatchId = "p-1",
        Revision = 1,
        TransactionStatus = PatchTransactionStatuses.AwaitingAcceptance,
    };

    [Fact]
    public void Current_is_null_initially()
    {
        var store = new InMemoryHeldPatchInteractionStore();
        Assert.Null(store.Current);
    }

    [Fact]
    public void Save_then_Current_returns_interaction()
    {
        var store = new InMemoryHeldPatchInteractionStore();
        var held = Sample();

        store.Save(held);

        Assert.Same(held, store.Current);
    }

    [Fact]
    public void Save_replaces_previous_interaction()
    {
        var store = new InMemoryHeldPatchInteractionStore();
        store.Save(Sample());
        var replacement = Sample() with { PatchId = "p-2" };

        store.Save(replacement);

        Assert.Equal("p-2", store.Current!.PatchId);
    }

    [Fact]
    public void Clear_removes_interaction()
    {
        var store = new InMemoryHeldPatchInteractionStore();
        store.Save(Sample());

        store.Clear();

        Assert.Null(store.Current);
    }

    [Fact]
    public void Save_rejects_null()
    {
        var store = new InMemoryHeldPatchInteractionStore();
        Assert.Throws<ArgumentNullException>(() => store.Save(null!));
    }
}
