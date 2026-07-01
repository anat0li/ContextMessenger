using ContextMessenger.App.Wpf.Services;
using ContextMessenger.App.Wpf.Settings;

namespace ContextMessenger.App.Wpf.Tests;

public sealed class SqlConnectionStringResolverTests
{
    [Fact]
    public void Resolve_supports_literal_reference()
    {
        var result = new SqlConnectionStringResolver().Resolve(new SqlRootSettings
        {
            ConnectionStringRef = "literal:Data Source=test.db",
        });

        Assert.Equal("Data Source=test.db", result);
    }

    [Fact]
    public void Resolve_supports_environment_reference()
    {
        var name = "CONTEXT_MESSENGER_TEST_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(name, "Server=test");
        try
        {
            var result = new SqlConnectionStringResolver().Resolve(new SqlRootSettings
            {
                ConnectionStringRef = $"env:{name}",
            });

            Assert.Equal("Server=test", result);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }
}
