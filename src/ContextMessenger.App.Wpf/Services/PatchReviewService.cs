using ContextMessenger.App.Wpf.Patching;
using ContextMessenger.App.Wpf.ViewModels;

namespace ContextMessenger.App.Wpf.Services;

public sealed class PatchReviewService
{
    private readonly FileHeldReviewStore _fileStore;
    private bool _suppressPersist;

    public PatchReviewService(FileHeldReviewStore fileStore)
    {
        _fileStore = fileStore ?? throw new ArgumentNullException(nameof(fileStore));
        PatchReview = new PatchReviewViewModel(Router);
        PatchReview.Changed += (_, _) => Persist();
        Store.Changed += (_, _) => Persist();
    }

    public InMemoryHeldPatchInteractionStore Store { get; } = new();

    public HeldPatchActionsRouter Router { get; } = new();

    public PatchReviewViewModel PatchReview { get; }

    public void Project(IHeldPatchActions? ownerActions)
    {
        Router.Target = Store.Current is null ? null : ownerActions;
        PatchReview.Update(Store.Current);
        // Keep the durable slot correct even when Update is a no-op and emits no Changed event.
        Persist();
    }

    public void RefreshProjection()
    {
        if (Store.Current is null)
            Router.Target = null;

        PatchReview.Update(Store.Current);
        // Keep the durable slot correct even when Update is a no-op and emits no Changed event.
        Persist();
    }

    public RestoredHeldReviewOwner? Restore()
    {
        var state = _fileStore.Load();
        if (state is null)
            return null;

        _suppressPersist = true;
        try
        {
            Store.Save(state.Interaction);
            PatchReview.RestoreState(state);
        }
        finally
        {
            _suppressPersist = false;
        }

        Persist();
        return new RestoredHeldReviewOwner(state.Interaction.TargetName, state.Interaction.RootName);
    }

    private void Persist()
    {
        if (_suppressPersist)
            return;

        var interaction = Store.Current;
        if (interaction is null)
        {
            _fileStore.Clear();
            return;
        }

        _fileStore.Save(HeldReviewState.From(interaction, PatchReview.Comments));
    }
}

public sealed record RestoredHeldReviewOwner(string TargetName, string RootName);
