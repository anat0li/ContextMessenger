using System.IO;
using ContextMessenger.App.Wpf.Logging;
using ContextMessenger.App.Wpf.Services;
using ContextMessenger.App.Wpf.Settings;
using Microsoft.Extensions.Logging;

namespace ContextMessenger.App.Wpf.Tests;

public sealed class LoopLogStoreClearTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "CmLogTests_" + Guid.NewGuid().ToString("N"));

    private LoopLogStore NewStore() => new(_dir, 524_288, new AppSettings());

    private static LogEntry Entry(string message) => new()
    {
        Timestamp = DateTimeOffset.Now,
        Level = LogLevel.Information,
        Kind = LogEntryKind.Info,
        Message = message,
    };

    [Fact]
    public void Clear_archives_current_log_and_starts_fresh()
    {
        var store = NewStore();
        store.Append("ChatGPT", "TestRoot", Entry("first line"));
        var path = Path.Combine(_dir, "ChatGPT-TestRoot.log");
        Assert.True(File.Exists(path));

        store.Clear("ChatGPT", "TestRoot");

        // Original archived to {stem}1.log; the live file is gone until the next append.
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(Path.Combine(_dir, "ChatGPT-TestRoot1.log")));

        // Next append starts a brand-new live file with only the new content.
        store.Append("ChatGPT", "TestRoot", Entry("second line"));
        Assert.True(File.Exists(path));
        var content = File.ReadAllText(path);
        Assert.Contains("second line", content);
        Assert.DoesNotContain("first line", content);
    }

    [Fact]
    public void Clear_archives_to_incrementing_indexes()
    {
        var store = NewStore();
        store.Append("T", "R", Entry("a"));
        store.Clear("T", "R");
        store.Append("T", "R", Entry("b"));
        store.Clear("T", "R");

        Assert.True(File.Exists(Path.Combine(_dir, "T-R1.log")));
        Assert.True(File.Exists(Path.Combine(_dir, "T-R2.log")));
    }

    [Fact]
    public void Clear_is_noop_when_no_log_exists()
    {
        var store = NewStore();
        store.Clear("T", "R"); // must not throw
        Assert.False(File.Exists(Path.Combine(_dir, "T-R.log")));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
