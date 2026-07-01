using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ContextMessenger.App.Wpf.Settings;

namespace ContextMessenger.App.Wpf.Services;

public sealed class FileSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string SettingsPath { get; }
    private string? LegacySettingsPath { get; }

    public FileSettingsStore()
        : this(DefaultSettingsPath(), LegacyDefaultSettingsPath())
    {
    }

    public FileSettingsStore(string settingsPath)
        : this(settingsPath, null)
    {
    }

    private FileSettingsStore(string settingsPath, string? legacySettingsPath)
    {
        SettingsPath = settingsPath;
        LegacySettingsPath = legacySettingsPath;
    }

    public AppSettings Load()
    {
        MigrateLegacySettings();
        var settings = ReadFromDisk();
        return ApplyDefaults(settings);
    }

    private AppSettings ReadFromDisk()
    {
        if (!File.Exists(SettingsPath)) return new AppSettings();
        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // Corrupted settings file — start fresh.
            return new AppSettings();
        }
    }

    /// <summary>
    /// Supplies default <see cref="TargetProfile"/>s and migrates the legacy
    /// <see cref="AppSettings.LastRoot"/> into a <see cref="RootProfile"/>.
    /// In-memory only — Save is not triggered here, so the on-disk shape is
    /// not altered until a consumer explicitly calls <see cref="Save"/>.
    /// </summary>
    private static AppSettings ApplyDefaults(AppSettings settings)
    {
        var targets = settings.Targets;
        var roots = settings.Roots;
        var currentRootName = settings.CurrentRootName;
        var logging = MigrateLogging(settings);
        var changed = false;

        if (targets.Count == 0)
        {
            targets =
            [
                new TargetProfile
                {
                    Name = "ChatGPT",
                    ProcessName = "ChatGPT",
                    Description = "ChatGPT Desktop application",
                },
            ];
            changed = true;
        }

        var defaultedTargets = ApplyTargetAutomationDefaults(targets);
        if (!ReferenceEquals(defaultedTargets, targets))
        {
            targets = defaultedTargets;
            changed = true;
        }

        if (roots.Count == 0 && !string.IsNullOrEmpty(settings.LastRoot))
        {
            roots = [new RootProfile { Name = "Default", Path = settings.LastRoot }];
            if (string.IsNullOrEmpty(currentRootName))
                currentRootName = "Default";
            changed = true;
        }

        if (!string.IsNullOrEmpty(currentRootName) &&
            settings.AutoProcessEnabled is true &&
            targets.Count > 0)
        {
            var currentTargetName = settings.CurrentTargetName;
            targets = targets.Select(target =>
                string.Equals(target.Name, currentTargetName, StringComparison.Ordinal) ||
                (string.IsNullOrEmpty(currentTargetName) && ReferenceEquals(target, targets.First()))
                    ? target with
                    {
                        Roots = MergeRootSettings(
                            target.Roots,
                            new TargetRootSettings
                            {
                                RootName = currentRootName,
                                AutoProcessEnabled = true,
                            }),
                    }
                    : target).ToArray();
            changed = true;
        }

        if (!Equals(logging, settings.Logging))
            changed = true;

        if (!changed) return settings;

        return settings with
        {
            Targets = targets,
            Roots = roots,
            CurrentRootName = currentRootName,
            Logging = logging,
            AutoProcessEnabled = null,
            LogTimestampFormat = null,
            LogFileTimestampFormat = null,
            ShowLogLevel = null,
            EnableFileLogging = null,
            EnableDebugOutputLogging = null,
        };
    }

    private static IReadOnlyList<TargetRootSettings> MergeRootSettings(
        IReadOnlyList<TargetRootSettings> roots,
        TargetRootSettings root)
    {
        if (roots.Any(r => string.Equals(r.RootName, root.RootName, StringComparison.Ordinal)))
        {
            return roots.Select(r => string.Equals(r.RootName, root.RootName, StringComparison.Ordinal)
                    ? root
                    : r)
                .ToArray();
        }

        return roots.Concat([root]).ToArray();
    }

    private static LoggingSettings MigrateLogging(AppSettings settings)
    {
        var logging = settings.Logging;
        if (!string.IsNullOrWhiteSpace(settings.LogTimestampFormat))
            logging = logging with { LogTimestampFormat = settings.LogTimestampFormat };
        if (!string.IsNullOrWhiteSpace(settings.LogFileTimestampFormat))
            logging = logging with { LogFileTimestampFormat = settings.LogFileTimestampFormat };
        if (settings.ShowLogLevel is { } showLogLevel)
            logging = logging with { ShowLogLevel = showLogLevel };
        if (settings.EnableFileLogging is { } enableFileLogging)
            logging = logging with { EnableFileLogging = enableFileLogging };
        if (settings.EnableDebugOutputLogging is { } enableDebugOutputLogging)
            logging = logging with { EnableDebugOutputLogging = enableDebugOutputLogging };

        return logging;
    }

    private static IReadOnlyList<TargetProfile> ApplyTargetAutomationDefaults(IReadOnlyList<TargetProfile> targets)
    {
        var changed = false;
        var defaultAutomation = new TargetAutomationSettings();
        var updated = targets.Select(target =>
        {
            if (target.Automation != defaultAutomation)
                return target;

            if (IsClaudeTarget(target))
            {
                changed = true;
                return target with
                {
                    Description = string.IsNullOrEmpty(target.Description)
                        ? "Claude Desktop application"
                        : target.Description,
                    Automation = defaultAutomation with
                    {
                        MessageAnchorText = "Claude responded: ",
                        ReadyAnchorText = "Write a message…",
                        InputEditName = "Write your prompt to Claude",
                        SendButtonName = "Send message",
                    },
                };
            }

            return target;
        }).ToArray();

        return changed ? updated : targets;
    }

    private static bool IsClaudeTarget(TargetProfile target) =>
        string.Equals(target.Name, "Claude", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(target.ProcessName, "Claude", StringComparison.OrdinalIgnoreCase);

    public void Save(AppSettings settings)
    {
        var dir = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(settings, WriteOptions);
        File.WriteAllText(SettingsPath, json);
    }

    private static string DefaultSettingsPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ContextMessenger",
        "appsettings.json");

    private static string LegacyDefaultSettingsPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ContextMessenger",
        "settings.json");

    private void MigrateLegacySettings()
    {
        if (LegacySettingsPath is null ||
            File.Exists(SettingsPath) ||
            !File.Exists(LegacySettingsPath))
        {
            return;
        }

        var dir = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.Copy(LegacySettingsPath, SettingsPath);
    }
}
