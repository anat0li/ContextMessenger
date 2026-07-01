namespace ContextMessenger.Core.Roslyn;

public sealed record GotoDefinitionQuery
{
    public string RelativePath { get; init; } = "";

    public int Line { get; init; }

    public int Column { get; init; }
}
