namespace PortalMonitor;

public sealed class MonitorTarget
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public int? Port { get; set; }

    // Values maintained at runtime and shown in the grid; they are not persisted.
    public string Status { get; set; } = "Nie sprawdzono";
    public string Reason { get; set; } = "";
    public string LastCheck { get; set; } = "";
    public string ResponseTime { get; set; } = "";

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Address : Name;
}
