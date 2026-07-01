using ContextMessenger.Core.Patching;
using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.Protocol.Commands;

public sealed class CurrentPatchCommandParams
{
}

internal sealed class CurrentPatchHandler : CommandHandlerBase<CurrentPatchCommandParams, PatchTransactionCommandResult>
{
    private readonly IPatchTransactionService _patches;

    public CurrentPatchHandler(IPatchTransactionService patches)
    {
        _patches = patches ?? throw new ArgumentNullException(nameof(patches));
    }

    public override string CommandType => CommandTypes.CurrentPatch;

    protected override PatchTransactionCommandResult ExecuteCore(CurrentPatchCommandParams parameters) =>
        PatchTransactionCommandResult.FromCore(_patches.Current());
}
