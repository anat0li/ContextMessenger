using ContextMessenger.Data;
using Microsoft.Data.Sqlite;
using System.Diagnostics;

namespace ContextMessenger.Data.Tests;

public sealed class DataQueryServiceTests
{
    [Fact]
    public void Execute_ReturnsRowsAndMapsCommonValues()
    {
        using var database = SqliteTestDatabase.Create();
        using var connection = database.OpenReadOnly();
        var service = new DataQueryService(new ReadOnlySqlGuard());

        var result = service.Execute(connection, """
            select
                Id,
                Name,
                Bio,
                Payload,
                CreatedUtc,
                Price,
                ExternalId,
                OptionalValue
            from People
            order by Id
            """, new DataConnectionSettings
        {
            MaxRows = 10,
            MaxCellBytes = 128
        });

        Assert.Equal(2, result.RowCount);
        Assert.False(result.Truncated);
        Assert.Equal("Id", result.Columns[0].Name);
        Assert.Equal(1L, result.Rows[0][0].Value);
        Assert.Equal("Ada", result.Rows[0][1].Value);
        Assert.Equal("AQIDBAU=", result.Rows[0][3].Value);
        Assert.Equal("12.34", result.Rows[0][5].Value);
        Assert.Equal("11111111-1111-1111-1111-111111111111", result.Rows[0][6].Value);
        Assert.Null(result.Rows[0][7].Value);
    }

    [Fact]
    public void Execute_EnforcesMaxRows()
    {
        using var database = SqliteTestDatabase.Create();
        using var connection = database.OpenReadOnly();
        var service = new DataQueryService(new ReadOnlySqlGuard());

        var result = service.Execute(connection, "select Id from People order by Id", new DataConnectionSettings
        {
            MaxRows = 1,
            MaxCellBytes = 128
        });

        Assert.Equal(1, result.RowCount);
        Assert.True(result.Truncated);
        Assert.Equal(1, result.Page.Limit);
        Assert.True(result.Page.HasNextPage);
    }

    [Fact]
    public void Execute_ReturnsRequestedPage()
    {
        using var database = SqliteTestDatabase.Create();
        using var connection = database.OpenReadOnly();
        var service = new DataQueryService(new ReadOnlySqlGuard());
        var settings = new DataConnectionSettings
        {
            MaxRows = 10,
            MaxCellBytes = 128
        };

        var firstPage = service.Execute(
            connection,
            "select Id, Name from People order by Id",
            settings,
            new DataQueryPageRequest(Offset: 0, Limit: 1));
        var secondPage = service.Execute(
            connection,
            "select Id, Name from People order by Id",
            settings,
            new DataQueryPageRequest(Offset: 1, Limit: 1));

        Assert.Equal("Ada", firstPage.Rows[0][1].Value);
        Assert.False(firstPage.Page.HasPreviousPage);
        Assert.True(firstPage.Page.HasNextPage);
        Assert.Equal("Linus", secondPage.Rows[0][1].Value);
        Assert.True(secondPage.Page.HasPreviousPage);
        Assert.False(secondPage.Page.HasNextPage);
    }

    [Fact]
    public void Execute_CapsRequestedPageSizeAtConfiguredMaximum()
    {
        using var database = SqliteTestDatabase.Create();
        using var connection = database.OpenReadOnly();
        var service = new DataQueryService(new ReadOnlySqlGuard());

        var result = service.Execute(
            connection,
            "select Id from People order by Id",
            new DataConnectionSettings
            {
                MaxRows = 1,
                MaxCellBytes = 128
            },
            new DataQueryPageRequest(Limit: 100));

        Assert.Single(result.Rows);
        Assert.Equal(1, result.Page.Limit);
        Assert.True(result.Page.HasNextPage);
    }

    [Fact]
    public void Execute_TruncatesOversizedTextAndBinaryCells()
    {
        using var database = SqliteTestDatabase.Create();
        using var connection = database.OpenReadOnly();
        var service = new DataQueryService(new ReadOnlySqlGuard());

        var result = service.Execute(connection, "select Bio, Payload from People where Id = 1", new DataConnectionSettings
        {
            MaxRows = 10,
            MaxCellBytes = 4
        });

        Assert.True(result.Truncated);
        Assert.True(result.Rows[0][0].Truncated);
        Assert.Equal(100, result.Rows[0][0].ByteSize);
        Assert.True(result.Rows[0][1].Truncated);
        Assert.Equal(5, result.Rows[0][1].ByteSize);
        Assert.False(result.Page.HasNextPage);
    }

    [Fact]
    public void Execute_RejectsMutationBeforeExecuting()
    {
        using var database = SqliteTestDatabase.Create();
        using var connection = database.OpenReadOnly();
        var service = new DataQueryService(new ReadOnlySqlGuard());

        Assert.Throws<ReadOnlySqlException>(() => service.Execute(connection, "delete from People", new DataConnectionSettings()));
    }

    [Fact]
    public void Execute_RejectsInvalidPageBounds()
    {
        using var database = SqliteTestDatabase.Create();
        using var connection = database.OpenReadOnly();
        var service = new DataQueryService(new ReadOnlySqlGuard());

        Assert.Throws<ArgumentOutOfRangeException>(() => service.Execute(
            connection,
            "select Id from People",
            new DataConnectionSettings(),
            new DataQueryPageRequest(Offset: -1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => service.Execute(
            connection,
            "select Id from People",
            new DataConnectionSettings(),
            new DataQueryPageRequest(Limit: 0)));
    }

    [Fact]
    public async Task Execute_CancelsRunningCommandAndReleasesConnection()
    {
        using var database = SqliteTestDatabase.Create();
        using var connection = database.OpenReadOnly();
        var service = new DataQueryService(new ReadOnlySqlGuard());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Run(() => service.Execute(
            connection,
            """
            with recursive Numbers(Value) as (
                select 1
                union all
                select Value + 1 from Numbers where Value < 100000000
            )
            select sum(Value) from Numbers
            """,
            new DataConnectionSettings
            {
                CommandTimeoutSeconds = 30,
                MaxRows = 1
            },
            cancellationToken: cancellation.Token)));

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Cancellation took {stopwatch.Elapsed}.");
        Assert.Equal(System.Data.ConnectionState.Open, connection.State);

        using var command = connection.CreateCommand();
        command.CommandText = "select count(*) from People";
        Assert.Equal(2L, command.ExecuteScalar());
    }
}
