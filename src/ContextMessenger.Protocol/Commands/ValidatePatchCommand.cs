using System.Text.Json.Serialization;
using ContextMessenger.Core.Patching;
using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.Protocol.Commands;

public sealed class ValidatePatchCommandParams
{
    [JsonPropertyName("patchId")]
    public string? PatchId { get; set; }

    [JsonPropertyName("baseRevision")]
    public int? BaseRevision { get; set; }

    [JsonPropertyName("files")]
    public IReadOnlyList<PatchFileOperationParams> Files { get; set; } = [];

    [JsonPropertyName("edits")]
    public IReadOnlyList<PatchEditOperationParams> Edits { get; set; } = [];

    [JsonPropertyName("build")]
    public PatchPolicyParams? Build { get; set; }

    [JsonPropertyName("tests")]
    public PatchPolicyParams? Tests { get; set; }
}

internal sealed class ValidatePatchHandler : CommandHandlerBase<ValidatePatchCommandParams, ValidatePatchCommandResult>
{
    private readonly IPatchTransactionService _patches;

    public ValidatePatchHandler(IPatchTransactionService patches)
    {
        _patches = patches ?? throw new ArgumentNullException(nameof(patches));
    }

    public override string CommandType => CommandTypes.ValidatePatch;

    protected override ValidatePatchCommandResult ExecuteCore(ValidatePatchCommandParams parameters)
    {
        var result = _patches.Validate(new ValidatePatchRequest
        {
            PatchId = parameters.PatchId,
            BaseRevision = parameters.BaseRevision,
            Files = parameters.Files.Select(f => f.ToCore()).ToArray(),
            Edits = parameters.Edits.Select(e => e.ToCore()).ToArray(),
            Build = parameters.Build?.ToCore(),
            Tests = parameters.Tests?.ToCore(),
        });

        return ValidatePatchCommandResult.FromCore(result);
    }
}
