using System.Text.Json.Serialization;
using ContextMessenger.Core.Patching;
using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.Protocol.Commands;

public sealed class AmendPatchCommandParams
{
    [JsonPropertyName("patchId")]
    public string PatchId { get; set; } = "";

    [JsonPropertyName("baseRevision")]
    public int BaseRevision { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("files")]
    public IReadOnlyList<PatchFileOperationParams> Files { get; set; } = [];

    [JsonPropertyName("edits")]
    public IReadOnlyList<PatchEditOperationParams> Edits { get; set; } = [];

    [JsonPropertyName("build")]
    public PatchPolicyParams? Build { get; set; }

    [JsonPropertyName("tests")]
    public PatchPolicyParams? Tests { get; set; }

    /// <summary>
    /// Optional model comments. Matching ids reply to existing review threads; unknown ids open
    /// new model-originated threads, optionally anchored with path/line.
    /// </summary>
    [JsonPropertyName("commentReplies")]
    public IReadOnlyList<CommentReplyParams> CommentReplies { get; set; } = [];
}

public sealed class CommentReplyParams
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("reply")]
    public string Reply { get; set; } = "";

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("line")]
    public int Line { get; set; }
}

internal sealed class AmendPatchHandler : CommandHandlerBase<AmendPatchCommandParams, PatchTransactionCommandResult>
{
    private readonly IPatchTransactionService _patches;

    public AmendPatchHandler(IPatchTransactionService patches)
    {
        _patches = patches ?? throw new ArgumentNullException(nameof(patches));
    }

    public override string CommandType => CommandTypes.AmendPatch;

    protected override PatchTransactionCommandResult ExecuteCore(AmendPatchCommandParams parameters)
    {
        var result = _patches.Amend(new AmendPatchRequest
        {
            PatchId = parameters.PatchId,
            BaseRevision = parameters.BaseRevision,
            Description = parameters.Description,
            Files = parameters.Files.Select(f => f.ToCore()).ToArray(),
            Edits = parameters.Edits.Select(e => e.ToCore()).ToArray(),
            Build = parameters.Build?.ToCore(),
            Tests = parameters.Tests?.ToCore(),
        });

        return PatchTransactionCommandResult.FromCore(result);
    }
}
