namespace ContextMessenger.Data;

public sealed record DataConnectionSettings
{
    public string ConnectionString { get; init; } = "";
    public bool ReadOnly { get; init; } = true;
    public int CommandTimeoutSeconds { get; init; } = 30;
    public int MaxRows { get; init; } = 100;
    public int MaxCellBytes { get; init; } = 65536;
}
