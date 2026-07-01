namespace ContextMessenger.App.Wpf.Patching;

/// <summary>What the host should do with a processed response that carried a patch outcome.</summary>
public enum PatchHoldDecision
{
    /// <summary>Submit the response to the chat target as usual.</summary>
    Deliver,

    /// <summary>Hold the response for human review; do not submit.</summary>
    Hold,
}
