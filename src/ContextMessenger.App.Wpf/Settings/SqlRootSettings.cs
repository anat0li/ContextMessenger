using System.Text.Json.Serialization;

namespace ContextMessenger.App.Wpf.Settings;

public sealed record SqlRootSettings
{
    public string ProviderInvariantName { get; init; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProviderAssemblyPath { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProviderFactoryTypeName { get; init; }

    public string ConnectionStringRef { get; init; } = "";

    public bool ReadOnly { get; init; } = true;

    public int CommandTimeoutSeconds { get; init; } = 30;

    public int MaxRows { get; init; } = 100;

    public int MaxCellBytes { get; init; } = 65_536;

    public bool AllowSchemaCommands { get; init; } = true;
}
