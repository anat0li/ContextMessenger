using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ContextMessenger.App.Wpf.Patching;

namespace ContextMessenger.App.Wpf.Services;

public sealed class FileHeldReviewStore
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string StatePath { get; }

    public FileHeldReviewStore()
        : this(DefaultStatePath())
    {
    }

    public FileHeldReviewStore(string statePath)
    {
        StatePath = statePath ?? throw new ArgumentNullException(nameof(statePath));
    }

    public HeldReviewState? Load()
    {
        if (!File.Exists(StatePath))
            return null;

        try
        {
            var json = File.ReadAllText(StatePath);
            return JsonSerializer.Deserialize<HeldReviewState>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Save(HeldReviewState? state)
    {
        if (state is null)
        {
            Clear();
            return;
        }

        var dir = Path.GetDirectoryName(StatePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(state, WriteOptions);
        File.WriteAllText(StatePath, json);
    }

    public void Clear()
    {
        if (File.Exists(StatePath))
            File.Delete(StatePath);
    }

    private static string DefaultStatePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ContextMessenger",
        "held-review.json");
}
