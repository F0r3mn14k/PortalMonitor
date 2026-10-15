namespace PortalMonitor;

public sealed class LogWriter
{
    private readonly string _logDirectory;

    public LogWriter()
    {
        // Logs stay beside the program so a portable copy keeps its own history.
        // A daily directory is selected in Write because the date can change while the app is open.
        _logDirectory = Path.Combine(AppContext.BaseDirectory, "LOG");
    }

    public string Write(MonitorTarget target, CheckResult result)
    {
        var now = DateTime.Now;
        var dailyDirectory = Path.Combine(_logDirectory, now.ToString("yyyy"), now.ToString("MM"));
        Directory.CreateDirectory(dailyDirectory);
        var line = $"{now:yyyy-MM-dd HH:mm:ss} | {result.Status} | {target.DisplayName} | {target.Address}" +
                   $" | {result.Reason}" +
                   (result.Duration is null ? "" : $" | {result.Duration.Value.TotalMilliseconds:F0} ms");
        File.AppendAllText(Path.Combine(dailyDirectory, $"{now:yyyy-MM-dd}.log"), line + Environment.NewLine);
        return line;
    }

    public string DirectoryPath
    {
        get
        {
            Directory.CreateDirectory(_logDirectory);
            return _logDirectory;
        }
    }
}
