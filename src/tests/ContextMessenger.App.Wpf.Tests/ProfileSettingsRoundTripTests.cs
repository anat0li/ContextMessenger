using System.Text.Json;
using ContextMessenger.Core.Patching;
using ContextMessenger.App.Wpf.Settings;
using ContextMessenger.Core.Meta;

namespace ContextMessenger.App.Wpf.Tests;

public sealed class ProfileSettingsRoundTripTests
{
    [Fact]
    public void RootProfile_round_trips_description()
    {
        var original = new RootProfile
        {
            Name = "Main",
            Path = "C:/repo",
            Description = "Main repository",
        };

        var json = JsonSerializer.Serialize(original);
        var parsed = JsonSerializer.Deserialize<RootProfile>(json);

        Assert.NotNull(parsed);
        Assert.Equal("Main", parsed!.Name);
        Assert.Equal("C:/repo", parsed.Path);
        Assert.Equal("Main repository", parsed.Description);
    }

    [Fact]
    public void RootProfile_without_description_round_trips_as_null()
    {
        var parsed = JsonSerializer.Deserialize<RootProfile>("""{"Name":"X","Path":"C:/x"}""");

        Assert.NotNull(parsed);
        Assert.Null(parsed!.Description);
        Assert.Equal(RootKind.FileSystem, parsed.Kind);
    }

    [Fact]
    public void Sql_root_round_trips_settings()
    {
        var original = new RootProfile
        {
            Name = "Northwind",
            Kind = RootKind.Sql,
            Sql = new SqlRootSettings
            {
                ProviderInvariantName = "Microsoft.Data.Sqlite",
                ConnectionStringRef = "literal:Data Source=northwind.db",
                MaxRows = 25,
            },
        };

        var json = JsonSerializer.Serialize(original);
        var parsed = JsonSerializer.Deserialize<RootProfile>(json);

        Assert.NotNull(parsed);
        Assert.Equal(RootKind.Sql, parsed!.Kind);
        Assert.Equal("Microsoft.Data.Sqlite", parsed.Sql!.ProviderInvariantName);
        Assert.Equal(25, parsed.Sql.MaxRows);
    }

    [Fact]
    public void TargetProfile_round_trips_description()
    {
        var original = new TargetProfile
        {
            Name = "ChatGPT",
            ProcessName = "ChatGPT.exe",
            Description = "Desktop client",
        };

        var json = JsonSerializer.Serialize(original);
        var parsed = JsonSerializer.Deserialize<TargetProfile>(json);

        Assert.NotNull(parsed);
        Assert.Equal("Desktop client", parsed!.Description);
    }

    [Fact]
    public void TargetProfile_without_description_round_trips_as_null()
    {
        var parsed = JsonSerializer.Deserialize<TargetProfile>(
            """{"Name":"X","ProcessName":"X","Roots":[]}""");

        Assert.NotNull(parsed);
        Assert.Null(parsed!.Description);
    }

    [Fact]
    public void AppSettings_round_trips_active_patch_metadata()
    {
        var original = new AppSettings
        {
            ActivePatch = new PatchSessionMetadata
            {
                PatchId = "p-1",
                RootName = "Root",
                Status = "needs_revision",
                Revision = 2,
                BaseHeadSha = "abc",
                CreatedAtUtc = DateTime.UnixEpoch,
                UpdatedAtUtc = DateTime.UnixEpoch.AddMinutes(1),
                LastFailureStage = "build",
            },
        };

        var json = JsonSerializer.Serialize(original);
        var parsed = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(parsed?.ActivePatch);
        Assert.Equal("p-1", parsed!.ActivePatch!.PatchId);
        Assert.Equal("Root", parsed.ActivePatch.RootName);
        Assert.Equal("needs_revision", parsed.ActivePatch.Status);
        Assert.Equal("build", parsed.ActivePatch.LastFailureStage);
    }
}
