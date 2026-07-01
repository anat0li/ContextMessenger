using ContextMessenger.Core.Patching;

namespace ContextMessenger.App.Wpf.Services;

/// <summary>
/// The per-(target, root) services a loop needs: the request processor (for the watcher
/// loop) and the patch transaction service (for the review actions and the hold-for-review
/// flag), which share the same dispatcher state.
/// </summary>
public sealed record LoopSession(IRequestProcessor Processor, IPatchTransactionService? Patches);
