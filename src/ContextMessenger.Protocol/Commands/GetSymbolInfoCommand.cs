using System.Text.Json.Serialization;
using ContextMessenger.Core.Roslyn;
using ContextMessenger.Protocol.Dispatch;

namespace ContextMessenger.Protocol.Commands;

public sealed class GetSymbolInfoCommandParams
{
    [JsonPropertyName("symbolId")]
    public string SymbolId { get; set; } = "";
}

internal sealed class GetSymbolInfoHandler : CommandHandlerBase<GetSymbolInfoCommandParams, SymbolInfoResult>
{
    private readonly IRoslynNavigationService _roslyn;

    public GetSymbolInfoHandler(IRoslynNavigationService roslyn)
    {
        _roslyn = roslyn ?? throw new ArgumentNullException(nameof(roslyn));
    }

    public override string CommandType => CommandTypes.GetSymbolInfo;

    protected override SymbolInfoResult ExecuteCore(GetSymbolInfoCommandParams parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters.SymbolId))
            throw new ProtocolException(ProtocolErrorCodes.InvalidParameters, "symbolId is required.");

        return _roslyn.GetSymbolInfo(new GetSymbolInfoQuery
        {
            SymbolId = parameters.SymbolId,
        });
    }
}
