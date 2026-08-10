using System.Text.Json;

namespace EqSync.Core;

public sealed record TrustedPeer(string MachineId, string MachineName, DateTimeOffset PairedAtUtc);

public sealed record LocalAppSettings(
    string MachineId,
    IReadOnlyList<string> ManualInstallPaths,
    IReadOnlyList<TrustedPeer> TrustedPeers,
    bool FirewallPromptDismissed = false)
{
    public static LocalAppSettings CreateDefault()
    {
        return new LocalAppSettings(Guid.NewGuid().ToString("N"), [], []);
    }
}

public sealed class LocalAppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string SettingsPath { get; }

    public LocalAppSettingsStore(string? settingsPath = null)
    {
        SettingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EqSync",
            "settings.json");
    }

    public LocalAppSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return LocalAppSettings.CreateDefault();
        }

        string json = File.ReadAllText(SettingsPath);
        return JsonSerializer.Deserialize<LocalAppSettings>(json, JsonOptions) ?? LocalAppSettings.CreateDefault();
    }

    public void Save(LocalAppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
