using System.Text.Json.Nodes;

namespace ContextMessenger.Core.Patching;

public sealed record PatchEditOperation
{
    public string Path { get; init; } = "";

    public required string Kind { get; init; }

    public string? OldText { get; init; }

    public string? NewText { get; init; }

    public string? Anchor { get; init; }

    public string? Text { get; init; }

    public string? ExpectedFileHash { get; init; }

    public string? ExpectedAnchorHash { get; init; }

    public int? StartLine { get; init; }

    public int? EndLine { get; init; }

    public string? OldRangeHash { get; init; }

    public string? Pointer { get; init; }

    public bool ValueSpecified { get; init; }

    public JsonNode? Value { get; init; }

    public string? SymbolId { get; init; }

    public string? Name { get; init; }

    public string Match { get; init; } = "exact";

    public IReadOnlyList<string> Kinds { get; init; } = [];

    public string? Project { get; init; }

    public bool IncludeNonPublic { get; init; } = true;

    public int? Line { get; init; }

    public int? Column { get; init; }

    public string? OldSourceHash { get; init; }
}
