using System.Text.Json.Serialization;
using ContextMessenger.Core.Patching;
using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.Protocol.Commands;

public sealed class RevertPatchCommandParams
{
    [JsonPropertyName("patchId")]
    public string PatchId { get; set; } = "";
}

internal sealed class RevertPatchHandler : CommandHandlerBase<RevertPatchCommandParams, PatchTransactionCommandResult>
{
    private readonly IPatchTransactionService _patches;

    public RevertPatchHandler(IPatchTransactionService patches)
    {
        _patches = patches ?? throw new ArgumentNullException(nameof(patches));
    }

    public override string CommandType => CommandTypes.RevertPatch;

    protected override PatchTransactionCommandResult ExecuteCore(RevertPatchCommandParams parameters) =>
        PatchTransactionCommandResult.FromCore(_patches.Revert(parameters.PatchId));
}
