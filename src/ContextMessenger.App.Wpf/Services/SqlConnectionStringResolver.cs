using ContextMessenger.App.Wpf.Settings;

namespace ContextMessenger.App.Wpf.Services;

public sealed class SqlConnectionStringResolver : ISqlConnectionStringResolver
{
    private const string EnvironmentPrefix = "env:";
    private const string LiteralPrefix = "literal:";

    public string Resolve(SqlRootSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var reference = settings.ConnectionStringRef;
        if (reference.StartsWith(EnvironmentPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var variableName = reference[EnvironmentPrefix.Length..].Trim();
            if (variableName.Length == 0)
                throw new InvalidOperationException("SQL connection string environment variable name is required.");

            return Environment.GetEnvironmentVariable(variableName)
                ?? throw new InvalidOperationException(
                    $"SQL connection string environment variable '{variableName}' is not defined.");
        }

        if (reference.StartsWith(LiteralPrefix, StringComparison.OrdinalIgnoreCase))
            return reference[LiteralPrefix.Length..];

        throw new InvalidOperationException(
            "SQL connection string reference must use the 'env:' or 'literal:' prefix.");
    }
}
