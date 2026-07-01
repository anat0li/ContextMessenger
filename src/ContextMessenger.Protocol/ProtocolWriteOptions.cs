namespace ContextMessenger.Protocol;

public sealed record ProtocolWriteOptions
{
    public bool CompressLargeResponses { get; init; }
    public int CompressionThresholdBytes { get; init; } = 32_768;
}
