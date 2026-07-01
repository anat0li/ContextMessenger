using System.IO;
using System.Text.Json;
using ContextMessenger.App.Wpf.Settings;
using ContextMessenger.App.Wpf.Services;

namespace ContextMessenger.App.Wpf.Tests;

public sealed class FileSettingsStoreDefaultsTests
{
    [Fact]
    public void Default_seeded_ChatGPT_target_has_description()
    {
        using var temp = new TempSettingsDirectory();
        var store = new FileSettingsStore(temp.SettingsPath);

        var loaded = store.Load();

        var chatgpt = loaded.Targets.Single(t => t.Name == "ChatGPT");
        Assert.False(string.IsNullOrEmpty(chatgpt.Description));
    }

    [Fact]
    public void TargetAutomationSettings_reads_single_anchor_string()
    {
        var settings = JsonSerializer.Deserialize<TargetAutomationSettings>(
            """{ "MessageAnchorText": "Copy message", "ReadyAnchorText": "Ready" }""")!;

        Assert.Equal(["Copy message"], settings.MessageAnchorText.Values);
        Assert.Equal(["Ready"], settings.ReadyAnchorText.Values);
    }

    [Fact]
    public void TargetAutomationSettings_reads_anchor_string_array()
    {
        var settings = JsonSerializer.Deserialize<TargetAutomationSettings>(
            """{ "MessageAnchorText": ["Copy message", "Pasted text.txt\nDocument"], "ReadyAnchorText": ["Ready", "Write a message…"] }""")!;

        Assert.Equal(["Copy message", "Pasted text.txt\nDocument"], settings.MessageAnchorText.Values);
        Assert.Equal(["Ready", "Write a message…"], settings.ReadyAnchorText.Values);
    }

    private sealed class TempSettingsDirectory : IDisposable
    {
        public string Path { get; }
        public string SettingsPath { get; }

        public TempSettingsDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "ContextMessengerAppWpfTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
            SettingsPath = System.IO.Path.Combine(Path, "appsettings.json");
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch { }
        }
    }
}
