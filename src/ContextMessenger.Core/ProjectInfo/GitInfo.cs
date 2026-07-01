namespace ContextMessenger.Core.ProjectInfo;

using System.Text.Json.Serialization;

public sealed record GitInfo
{
    [JsonPropertyName("isRepository")]
    public bool IsRepository { get; init; }

    [JsonPropertyName("branch")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Branch { get; init; }

    [JsonPropertyName("headSha")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HeadSha { get; init; }

    [JsonPropertyName("isDirty")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsDirty { get; init; }
}
