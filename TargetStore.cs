using System.Text.Json;

namespace PortalMonitor;

public sealed class TargetStore
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public TargetStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PortalMonitor");
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "monitors.json");
    }

    public List<MonitorTarget> Load()
    {
        if (!File.Exists(_filePath)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<MonitorTarget>>(File.ReadAllText(_filePath)) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public void Save(IEnumerable<MonitorTarget> targets)
    {
        var persisted = targets.Select(target => new MonitorTarget
        {
            Name = target.Name,
            Address = target.Address,
            Port = target.Port
        }).ToList();
        File.WriteAllText(_filePath, JsonSerializer.Serialize(persisted, _jsonOptions));
    }
}
