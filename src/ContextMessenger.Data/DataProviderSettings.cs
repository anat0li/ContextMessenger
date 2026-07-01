namespace ContextMessenger.Data;

public sealed record DataProviderSettings
{
    public string ProviderInvariantName { get; init; } = "";
    public string? ProviderAssemblyPath { get; init; }
    public string? ProviderFactoryTypeName { get; init; }
}
