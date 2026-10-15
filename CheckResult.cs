namespace PortalMonitor;

public sealed record CheckResult(
    bool IsAvailable,
    string Status,
    string Reason,
    TimeSpan? Duration);
