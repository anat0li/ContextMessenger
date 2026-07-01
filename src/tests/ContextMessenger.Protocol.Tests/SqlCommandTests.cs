using System.Text.Json;
using ContextMessenger.Data;
using ContextMessenger.Protocol.Commands;
using ContextMessenger.Protocol.Dispatch;
using ContextMessenger.Protocol.Wire;

namespace ContextMessenger.Protocol.Tests;

public sealed class SqlCommandTests
{
    [Fact]
    public void Sql_query_returns_page_and_compact_cells()
    {
        var data = new FakeDataRootSession
        {
            QueryResult = new DataQueryResult(
                [new DataQueryColumn("name", "TEXT"), new DataQueryColumn("payload", "TEXT")],
                [
                    [
                        new DataCellValue("alpha"),
                        new DataCellValue("cut", Truncated: true, ByteSize: 20),
                    ],
                ],
                RowCount: 1,
                Truncated: true,
                DurationMs: 7,
                new DataQueryPageInfo(10, 5, 1, HasPreviousPage: true, HasNextPage: true)),
        };
        var dispatcher = SqlDispatcher(data);

        var response = dispatcher.Dispatch(Request(
            CommandTypes.SqlQuery,
            ("sql", "select name, payload from items order by name"),
            ("offset", 10),
            ("limit", 5)));

        var result = Assert.Single(response.Results!);
        Assert.Equal(ProtocolStatus.Ok, result.Status);
        Assert.Equal(10, result.Payload["page"].GetProperty("offset").GetInt32());
        Assert.True(result.Payload["page"].GetProperty("hasNextPage").GetBoolean());
        var row = result.Payload["rows"][0];
        Assert.Equal("alpha", row[0].GetString());
        Assert.Equal("cut", row[1].GetProperty("value").GetString());
        Assert.True(row[1].GetProperty("truncated").GetBoolean());
        Assert.Equal(20, row[1].GetProperty("byteSize").GetInt32());
        Assert.Equal(10, data.LastPage!.Offset);
        Assert.Equal(5, data.LastPage.Limit);
    }

    [Fact]
    public void Sql_commands_filter_schema_metadata()
    {
        var data = new FakeDataRootSession
        {
            Schema = new DataSchemaInfo(
                ["Tables", "Columns"],
                [
                    new DataTableInfo("db", "dbo", "People", "TABLE"),
                    new DataTableInfo("db", "audit", "Events", "TABLE"),
                ],
                [
                    new DataColumnInfo("db", "dbo", "People", "Id", "int", 1, false),
                    new DataColumnInfo("db", "audit", "Events", "Id", "int", 1, false),
                ]),
        };
        var dispatcher = SqlDispatcher(data);

        var tables = dispatcher.Dispatch(Request(
            CommandTypes.SqlTables,
            ("schema", "dbo")));
        var columns = dispatcher.Dispatch(Request(
            CommandTypes.SqlColumns,
            ("table", "people"),
            ("schema", "dbo")));

        Assert.Equal("People", tables.Results![0].Payload["tables"][0].GetProperty("name").GetString());
        Assert.Equal("Id", columns.Results![0].Payload["columns"][0].GetProperty("name").GetString());
    }

    [Fact]
    public void Sql_columns_returns_table_not_found_for_unknown_scoped_table()
    {
        var data = new FakeDataRootSession
        {
            Schema = new DataSchemaInfo(
                ["Tables", "Columns"],
                [
                    new DataTableInfo("MainDb", "audit", "Events", "TABLE"),
                ],
                [
                    new DataColumnInfo("MainDb", "audit", "Events", "Id", "int", 1, false),
                ]),
        };
        var dispatcher = SqlDispatcher(data);

        var response = dispatcher.Dispatch(Request(
            CommandTypes.SqlColumns,
            ("table", "events"),
            ("catalog", "maindb"),
            ("schema", "dbo")));

        var result = Assert.Single(response.Results!);
        Assert.Equal(ProtocolStatus.Error, result.Status);
        Assert.Equal(ProtocolErrorCodes.SqlTableNotFound, result.Error!.Code);
    }

    [Fact]
    public void Sql_columns_matches_table_catalog_and_schema_case_insensitively()
    {
        var data = new FakeDataRootSession
        {
            Schema = new DataSchemaInfo(
                ["Tables", "Columns"],
                [
                    new DataTableInfo("MainDb", "dbo", "Events", "TABLE"),
                    new DataTableInfo("MainDb", "audit", "Events", "TABLE"),
                ],
                [
                    new DataColumnInfo("MainDb", "dbo", "Events", "PublicId", "int", 1, false),
                    new DataColumnInfo("MainDb", "audit", "Events", "AuditId", "int", 1, false),
                ]),
        };
        var dispatcher = SqlDispatcher(data);

        var response = dispatcher.Dispatch(Request(
            CommandTypes.SqlColumns,
            ("table", "events"),
            ("catalog", "maindb"),
            ("schema", "DBO")));

        var result = Assert.Single(response.Results!);
        Assert.Equal(ProtocolStatus.Ok, result.Status);
        var column = Assert.Single(result.Payload["columns"].EnumerateArray());
        Assert.Equal("PublicId", column.GetProperty("name").GetString());
    }

    [Fact]
    public void Sql_columns_returns_empty_columns_for_known_table_without_column_metadata()
    {
        var data = new FakeDataRootSession
        {
            Schema = new DataSchemaInfo(
                ["Tables"],
                [
                    new DataTableInfo(null, null, "OpaqueTable", "TABLE"),
                ],
                []),
        };
        var dispatcher = SqlDispatcher(data);

        var response = dispatcher.Dispatch(Request(
            CommandTypes.SqlColumns,
            ("table", "opaquetable")));

        var result = Assert.Single(response.Results!);
        Assert.Equal(ProtocolStatus.Ok, result.Status);
        Assert.Empty(result.Payload["columns"].EnumerateArray());
    }

    [Fact]
    public void Sql_dispatcher_exposes_only_session_and_sql_capabilities()
    {
        var dispatcher = SqlDispatcher(new FakeDataRootSession());

        var response = dispatcher.Dispatch(Request(CommandTypes.Capabilities));
        var commands = response.Results![0].Payload["commands"]
            .EnumerateArray()
            .Select(command => command.GetProperty("name").GetString())
            .ToArray();

        Assert.Contains(CommandTypes.SqlQuery, commands);
        Assert.Contains(CommandTypes.SqlSchema, commands);
        Assert.Contains(CommandTypes.Capabilities, commands);
        Assert.DoesNotContain(CommandTypes.Tree, commands);
        Assert.DoesNotContain(CommandTypes.ProposePatch, commands);
    }

    [Fact]
    public void Sql_capabilities_rejects_known_command_not_available_for_root()
    {
        var dispatcher = SqlDispatcher(new FakeDataRootSession());

        var response = dispatcher.Dispatch(Request(
            CommandTypes.Capabilities,
            ("command", CommandTypes.Tree)));

        var result = Assert.Single(response.Results!);
        Assert.Equal(ProtocolStatus.Error, result.Status);
        Assert.Equal(ProtocolErrorCodes.InvalidParameters, result.Error!.Code);
        Assert.Contains("not available for the active root", result.Error.Message);
    }

    [Fact]
    public void Schema_commands_can_be_disabled_per_root()
    {
        var dispatcher = CommandDispatcher.ForServices(
            fs: null,
            roslyn: null,
            session: null,
            gitStatus: null,
            patchTransactions: null,
            dataRootSession: new FakeDataRootSession(),
            allowSchemaCommands: false);

        Assert.Contains(CommandTypes.SqlQuery, dispatcher.RegisteredCommands);
        Assert.DoesNotContain(CommandTypes.SqlSchema, dispatcher.RegisteredCommands);
        Assert.DoesNotContain(CommandTypes.SqlTables, dispatcher.RegisteredCommands);
        Assert.DoesNotContain(CommandTypes.SqlColumns, dispatcher.RegisteredCommands);
    }

    [Fact]
    public void Cancellation_returns_operation_cancelled_for_original_command()
    {
        using var cts = new CancellationTokenSource();
        var data = new FakeDataRootSession { OnQuery = _ => cts.Cancel() };
        var dispatcher = SqlDispatcher(data);

        var response = dispatcher.Dispatch(
            Request(CommandTypes.SqlQuery, ("sql", "select 1")),
            cts.Token);

        var result = Assert.Single(response.Results!);
        Assert.Equal(0, result.CommandIndex);
        Assert.Equal(CommandTypes.SqlQuery, result.Type);
        Assert.Equal(ProtocolStatus.Error, result.Status);
        Assert.Equal(ProtocolErrorCodes.OperationCancelled, result.Error!.Code);
    }

    [Fact]
    public void Cancellation_ignores_remaining_commands_in_request()
    {
        using var cts = new CancellationTokenSource();
        var data = new FakeDataRootSession { OnQuery = _ => cts.Cancel() };
        var dispatcher = SqlDispatcher(data);
        var request = Request(CommandTypes.SqlQuery, ("sql", "select 1"));
        request.Commands.Add(new ContextCommand { Type = CommandTypes.SqlSchema });

        var response = dispatcher.Dispatch(request, cts.Token);

        Assert.Equal(2, response.Results!.Count);
        Assert.Equal(ProtocolErrorCodes.OperationCancelled, response.Results[0].Error!.Code);
        Assert.Equal(ProtocolStatus.Ignored, response.Results[1].Status);
        Assert.Contains("preceding operation was cancelled", response.Results[1].Payload["reason"].GetString());
    }

    [Fact]
    public void Process_requests_marks_generated_cancellation_response()
    {
        using var cts = new CancellationTokenSource();
        var data = new FakeDataRootSession { OnQuery = _ => cts.Cancel() };
        var dispatcher = SqlDispatcher(data);
        var id = Guid.NewGuid().ToString();
        var body = $$"""
            {
              "version": "1.0",
              "id": "{{id}}",
              "commands": [
                {
                  "type": "sql_query",
                  "sql": "select 1"
                }
              ]
            }
            """;

        var result = dispatcher.ProcessRequestsDetailed([body], cts.Token);

        Assert.True(result.IsCancellationResponse);
        Assert.Contains(id, result.ResponseText);
        Assert.Contains(ProtocolErrorCodes.OperationCancelled, result.ResponseText);
    }

    private static CommandDispatcher SqlDispatcher(IDataRootSession data) =>
        CommandDispatcher.ForServices(
            fs: null,
            roslyn: null,
            session: null,
            gitStatus: null,
            patchTransactions: null,
            dataRootSession: data);

    private static ContextRequest Request(string type, params (string Name, object? Value)[] parameters)
    {
        var command = new ContextCommand { Type = type };
        foreach (var (name, value) in parameters)
            command.Parameters[name] = JsonSerializer.SerializeToElement(value);

        return new ContextRequest
        {
            Id = Guid.NewGuid().ToString(),
            Commands = [command],
        };
    }

    private sealed class FakeDataRootSession : IDataRootSession
    {
        public DataSchemaInfo Schema { get; set; } = new([], [], []);

        public DataQueryResult QueryResult { get; set; } = new(
            [],
            [],
            0,
            false,
            0,
            new DataQueryPageInfo(0, 100, 0, false, false));

        public DataQueryPageRequest? LastPage { get; private set; }

        public Action<CancellationToken>? OnQuery { get; set; }

        public DataSchemaInfo ReadSchema(CancellationToken cancellationToken = default) => Schema;

        public DataQueryResult ExecuteQuery(
            string sql,
            DataQueryPageRequest? page = null,
            CancellationToken cancellationToken = default)
        {
            LastPage = page;
            OnQuery?.Invoke(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return QueryResult;
        }
    }
}
