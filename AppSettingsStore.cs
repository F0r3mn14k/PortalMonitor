using System.Text.Json;

namespace PortalMonitor;

public sealed class AppSettings
{
    public int IntervalMinutes { get; set; } = 15;
}

public sealed class AppSettingsStore
{
    private readonly string _filePath;

    public AppSettingsStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PortalMonitor");
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath));
                if (settings is not null && settings.IntervalMinutes is >= 1 and <= 1440) return settings;
            }
        }
        catch (JsonException)
        {
            // A malformed settings file falls back to a safe default.
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings) =>
        File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
}
