using ContextMessenger.Data;

namespace ContextMessenger.Data.Tests;

public sealed class ReadOnlySqlGuardTests
{
    private readonly ReadOnlySqlGuard guard = new();

    [Theory]
    [InlineData("select * from People")]
    [InlineData("with ActivePeople as (select * from People) select * from ActivePeople")]
    [InlineData("explain select * from People")]
    [InlineData("select ';' as Value;")]
    public void Validate_AllowsSingleReadOnlyStatements(string sql)
    {
        guard.Validate(sql);
    }

    [Theory]
    [InlineData("")]
    [InlineData("delete from People")]
    [InlineData("select * from People; delete from People")]
    [InlineData("/* hidden */ drop table People")]
    [InlineData("-- comment\r\nupdate People set Name = 'x'")]
    [InlineData("exec dbo.DoWork")]
    [InlineData("create table Other(Id int)")]
    public void Validate_RejectsNonReadOnlyStatements(string sql)
    {
        Assert.Throws<ReadOnlySqlException>(() => guard.Validate(sql));
    }
}
