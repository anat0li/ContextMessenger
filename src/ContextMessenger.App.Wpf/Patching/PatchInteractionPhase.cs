namespace ContextMessenger.App.Wpf.Patching;

/// <summary>
/// Whether the human holds the floor or the host is waiting for the model to reply.
/// Orthogonal to the patch transaction status. Under hold-off a patch never sits in
/// <see cref="Reviewing"/> (responses are auto-sent); under hold-on the human drives
/// the send transitions.
/// </summary>
public enum PatchInteractionPhase
{
    /// <summary>The human holds the floor; no non-terminal message is pending a model reply.</summary>
    Reviewing,

    /// <summary>A non-terminal message (needs-revision / reviewer comments) was sent; awaiting the model's amend.</summary>
    AwaitingModelReply,
}
