using System.Text.Json;

namespace PortalMonitor;

public sealed class ProfileStore
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public ProfileStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PortalMonitor");
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "profiles.json");
    }

    public void EnsureDefaultProfile(IEnumerable<MonitorTarget> legacyTargets)
    {
        var profiles = ReadAll();
        if (profiles.Count != 0) return;
        profiles["Domyślny"] = CopyTargets(legacyTargets);
        WriteAll(profiles);
    }

    public IReadOnlyList<string> GetNames() => ReadAll().Keys.OrderBy(name => name).ToList();

    public List<MonitorTarget> Load(string name)
    {
        var profiles = ReadAll();
        return profiles.TryGetValue(name, out var targets) ? CopyTargets(targets) : [];
    }

    public void Save(string name, IEnumerable<MonitorTarget> targets)
    {
        var profiles = ReadAll();
        profiles[name] = CopyTargets(targets);
        WriteAll(profiles);
    }

    public bool Create(string name)
    {
        var profiles = ReadAll();
        if (profiles.ContainsKey(name)) return false;
        profiles[name] = [];
        WriteAll(profiles);
        return true;
    }

    public bool Delete(string name)
    {
        var profiles = ReadAll();
        if (!profiles.Remove(name)) return false;
        WriteAll(profiles);
        return true;
    }

    private Dictionary<string, List<MonitorTarget>> ReadAll()
    {
        if (!File.Exists(_filePath)) return new Dictionary<string, List<MonitorTarget>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, List<MonitorTarget>>>(File.ReadAllText(_filePath))
                   ?? new Dictionary<string, List<MonitorTarget>>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, List<MonitorTarget>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void WriteAll(Dictionary<string, List<MonitorTarget>> profiles) =>
        File.WriteAllText(_filePath, JsonSerializer.Serialize(profiles, _jsonOptions));

    private static List<MonitorTarget> CopyTargets(IEnumerable<MonitorTarget> targets) =>
        targets.Select(target => new MonitorTarget
        {
            Name = target.Name,
            Address = target.Address,
            Port = target.Port
        }).ToList();
}
