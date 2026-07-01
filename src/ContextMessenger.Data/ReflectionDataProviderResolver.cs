using System.Collections.Concurrent;
using System.Data.Common;
using System.Reflection;

namespace ContextMessenger.Data;

public sealed class ReflectionDataProviderResolver : IDataProviderResolver
{
    private static readonly IReadOnlyDictionary<string, KnownProvider> KnownProviders =
        new Dictionary<string, KnownProvider>(StringComparer.OrdinalIgnoreCase)
        {
            ["Microsoft.Data.Sqlite"] = new("Microsoft.Data.Sqlite", "Microsoft.Data.Sqlite.SqliteFactory"),
            ["Microsoft.Data.SqlClient"] = new("Microsoft.Data.SqlClient", "Microsoft.Data.SqlClient.SqlClientFactory"),
            ["MySql.Data"] = new("MySql.Data", "MySql.Data.MySqlClient.MySqlClientFactory")
        };

    private readonly ConcurrentDictionary<string, DbProviderFactory> factories = new(StringComparer.OrdinalIgnoreCase);

    public DbProviderFactory Resolve(DataProviderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (string.IsNullOrWhiteSpace(settings.ProviderInvariantName))
        {
            throw new DataProviderException("Provider invariant name is required.");
        }

        var cacheKey = string.Join("|", settings.ProviderInvariantName, settings.ProviderAssemblyPath, settings.ProviderFactoryTypeName);
        return factories.GetOrAdd(cacheKey, _ => ResolveCore(settings));
    }

    private static DbProviderFactory ResolveCore(DataProviderSettings settings)
    {
        var failures = new List<Exception>();

        if (KnownProviders.TryGetValue(settings.ProviderInvariantName, out var knownProvider))
        {
            var factory = TryCreateFactoryFromAssemblyName(knownProvider.AssemblyName, settings.ProviderFactoryTypeName ?? knownProvider.FactoryTypeName, failures);
            if (factory is not null)
            {
                return factory;
            }
        }

        try
        {
            return DbProviderFactories.GetFactory(settings.ProviderInvariantName);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            failures.Add(ex);
        }

        if (!string.IsNullOrWhiteSpace(settings.ProviderAssemblyPath))
        {
            var factory = TryCreateFactoryFromPath(settings.ProviderAssemblyPath, settings.ProviderFactoryTypeName, failures);
            if (factory is not null)
            {
                return factory;
            }
        }

        throw new DataProviderException(
            $"Unable to resolve ADO.NET provider factory for '{settings.ProviderInvariantName}'.",
            new AggregateException(failures));
    }

    private static DbProviderFactory? TryCreateFactoryFromAssemblyName(string assemblyName, string? factoryTypeName, List<Exception> failures)
    {
        try
        {
            var assembly = Assembly.Load(new AssemblyName(assemblyName));
            return CreateFactoryFromAssembly(assembly, factoryTypeName);
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException or TypeLoadException or MissingMemberException or InvalidCastException)
        {
            failures.Add(ex);
            return null;
        }
    }

    private static DbProviderFactory? TryCreateFactoryFromPath(string assemblyPath, string? factoryTypeName, List<Exception> failures)
    {
        try
        {
            if (!File.Exists(assemblyPath))
            {
                throw new FileNotFoundException("Provider assembly path does not exist.", assemblyPath);
            }

            var assembly = Assembly.LoadFrom(assemblyPath);
            return CreateFactoryFromAssembly(assembly, factoryTypeName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileLoadException or BadImageFormatException or TypeLoadException or MissingMemberException or InvalidCastException)
        {
            failures.Add(ex);
            return null;
        }
    }

    private static DbProviderFactory CreateFactoryFromAssembly(Assembly assembly, string? factoryTypeName)
    {
        var type = ResolveFactoryType(assembly, factoryTypeName);
        var instanceMember = type.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
        if (instanceMember?.GetValue(null) is DbProviderFactory fieldFactory)
        {
            return fieldFactory;
        }

        var instanceProperty = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
        if (instanceProperty?.GetValue(null) is DbProviderFactory propertyFactory)
        {
            return propertyFactory;
        }

        throw new MissingMemberException(type.FullName, "Instance");
    }

    private static Type ResolveFactoryType(Assembly assembly, string? factoryTypeName)
    {
        if (!string.IsNullOrWhiteSpace(factoryTypeName))
        {
            return assembly.GetType(factoryTypeName, throwOnError: true, ignoreCase: false)!;
        }

        var factoryTypes = assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(DbProviderFactory).IsAssignableFrom(type))
            .ToArray();

        return factoryTypes.Length switch
        {
            1 => factoryTypes[0],
            0 => throw new TypeLoadException($"No DbProviderFactory type was found in assembly '{assembly.FullName}'."),
            _ => throw new TypeLoadException($"Multiple DbProviderFactory types were found in assembly '{assembly.FullName}'. Specify ProviderFactoryTypeName.")
        };
    }

    private sealed record KnownProvider(string AssemblyName, string FactoryTypeName);
}
