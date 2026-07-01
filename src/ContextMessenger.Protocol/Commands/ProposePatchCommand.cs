using System.Text.Json.Serialization;
using ContextMessenger.Core.Patching;
using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.Protocol.Commands;

public sealed class ProposePatchCommandParams
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("commitMessage")]
    public string? CommitMessage { get; set; }

    [JsonPropertyName("files")]
    public IReadOnlyList<PatchFileOperationParams> Files { get; set; } = [];

    [JsonPropertyName("edits")]
    public IReadOnlyList<PatchEditOperationParams> Edits { get; set; } = [];

    [JsonPropertyName("build")]
    public PatchPolicyParams Build { get; set; } = new();

    [JsonPropertyName("tests")]
    public PatchPolicyParams Tests { get; set; } = new();
}

internal sealed class ProposePatchHandler : CommandHandlerBase<ProposePatchCommandParams, PatchTransactionCommandResult>
{
    private readonly IPatchTransactionService _patches;

    public ProposePatchHandler(IPatchTransactionService patches)
    {
        _patches = patches ?? throw new ArgumentNullException(nameof(patches));
    }

    public override string CommandType => CommandTypes.ProposePatch;

    protected override PatchTransactionCommandResult ExecuteCore(ProposePatchCommandParams parameters)
    {
        var result = _patches.Propose(new ProposePatchRequest
        {
            Title = parameters.Title,
            Description = parameters.Description,
            CommitMessage = parameters.CommitMessage,
            Files = parameters.Files.Select(f => f.ToCore()).ToArray(),
            Edits = parameters.Edits.Select(e => e.ToCore()).ToArray(),
            Build = parameters.Build.ToCore(),
            Tests = parameters.Tests.ToCore(),
        });

        return PatchTransactionCommandResult.FromCore(result);
    }
}
