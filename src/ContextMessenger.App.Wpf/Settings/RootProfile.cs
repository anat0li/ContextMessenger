using System.Text.Json.Serialization;
using ContextMessenger.Core.Meta;

namespace ContextMessenger.App.Wpf.Settings;

public sealed record RootProfile
{
    public string Name { get; init; } = "";

    public string Path { get; init; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter<RootKind>))]
    public RootKind Kind { get; init; } = RootKind.FileSystem;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SqlRootSettings? Sql { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    /// <summary>
    /// When true, patch responses for this root are held for human review: passing patches
    /// defer acceptance (awaiting_acceptance) and the loop holds the response instead of
    /// submitting it. Per-root host policy, default off.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HoldPatchResponsesForReview { get; init; }
}
