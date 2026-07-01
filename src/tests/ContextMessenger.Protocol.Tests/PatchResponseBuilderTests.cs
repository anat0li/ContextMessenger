using ContextMessenger.Core.Patching;
using ContextMessenger.Protocol.Commands;

namespace ContextMessenger.Protocol.Tests;

public sealed class PatchResponseBuilderTests
{
    [Fact]
    public void Build_produces_correlated_begin_response()
    {
        var result = new PatchTransactionResult
        {
            PatchStatus = "accepted",
            PatchId = "p-1",
            Revision = 2,
            Applied = true,
            DiffVerified = true,
        };

        var text = PatchResponseBuilder.Build("req-123", CommandTypes.ProposePatch, 0, result);

        Assert.StartsWith(ProtocolDelimiters.BeginResponse, text);
        Assert.EndsWith(ProtocolDelimiters.EndResponse, text);
        Assert.Contains("\"id\": \"req-123\"", text);
        Assert.Contains("\"type\": \"propose_patch\"", text);
        Assert.Contains("\"patchStatus\": \"accepted\"", text);
        Assert.Contains("\"patchId\": \"p-1\"", text);
        Assert.Contains("\"revision\": 2", text);
    }

    [Fact]
    public void Build_uses_unknown_id_when_request_id_missing()
    {
        var result = new PatchTransactionResult { PatchStatus = "reverted" };

        var text = PatchResponseBuilder.Build("", CommandTypes.AmendPatch, 0, result);

        Assert.Contains("\"id\": \"unknown\"", text);
        Assert.Contains("\"type\": \"amend_patch\"", text);
        Assert.Contains("\"patchStatus\": \"reverted\"", text);
    }
}
