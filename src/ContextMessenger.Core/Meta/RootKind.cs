using System.Text.Json.Serialization;

namespace ContextMessenger.Core.Meta;

public enum RootKind
{
    [JsonStringEnumMemberName("fileSystem")]
    FileSystem,

    [JsonStringEnumMemberName("sql")]
    Sql,
}
