using ContextMessenger.App.Wpf.Patching;
using ContextMessenger.Core.Patching;

namespace ContextMessenger.App.Wpf.Services;

/// <summary>
/// The patch-side handles for a single processing loop: the loop's patch transaction service and
/// the held-patch actions that route review decisions (accept/revert/refresh) back to it.
/// </summary>
public sealed record LoopPatchContext(IPatchTransactionService Patches, IHeldPatchActions Actions);
