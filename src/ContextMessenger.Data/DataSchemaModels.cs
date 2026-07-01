using System.Text.Json.Serialization;

namespace ContextMessenger.Data;

public sealed record DataTableInfo(
    [property: JsonPropertyName("catalog")] string? Catalog,
    [property: JsonPropertyName("schema")] string? Schema,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string? Type);

public sealed record DataColumnInfo(
    [property: JsonPropertyName("catalog")] string? Catalog,
    [property: JsonPropertyName("schema")] string? Schema,
    [property: JsonPropertyName("tableName")] string? TableName,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("dataType")] string? DataType,
    [property: JsonPropertyName("ordinal")] int? Ordinal,
    [property: JsonPropertyName("isNullable")] bool? IsNullable);

public sealed record DataSchemaInfo(
    [property: JsonPropertyName("collections")] IReadOnlyList<string> Collections,
    [property: JsonPropertyName("tables")] IReadOnlyList<DataTableInfo> Tables,
    [property: JsonPropertyName("columns")] IReadOnlyList<DataColumnInfo> Columns);
