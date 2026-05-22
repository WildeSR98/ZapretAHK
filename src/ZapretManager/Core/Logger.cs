namespace ZapretManager.Core;

public static class Logger
{
    private static StreamWriter? _writer;
    private static bool _verbose;
    private static readonly object _lock = new();
    private static string? _logDir;

    public static void Init(string rootDir, bool verbose = false, int retentionDays = 14)
    {
        _verbose = verbose;
        _logDir = Path.Combine(rootDir, "logs");
        Directory.CreateDirectory(_logDir);

        // Rotate old logs
        RotateLogs(_logDir, retentionDays);

        var ts = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var logFile = Path.Combine(_logDir, $"zapret_{ts}.log");

        _writer = new StreamWriter(logFile, append: true, System.Text.Encoding.UTF8)
        {
            AutoFlush = true
        };

        Write("INFO", "=== ZAPRET MANAGER started ===");
        Write("INFO", $"OS: {Environment.OSVersion} | .NET: {Environment.Version}");
    }

    public static void Write(string level, string message)
    {
        if (_writer == null) return;
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
        lock (_lock)
        {
            try { _writer.WriteLine(line); }
            catch (Exception ex)
            {
                // Last-resort: write to stderr so it is not fully lost
                Console.Error.WriteLine($"[Logger] Write failed: {ex.Message}");
            }
        }
    }

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg) => Write("ERROR", msg);
    public static void Ok(string msg) => Write("OK", msg);
    public static void Step(string msg) => Write("STEP", msg);
    public static void Debug(string msg) { if (_verbose) Write("DEBUG", msg); }

    /// <summary>Flush and close the log writer (for graceful shutdown).</summary>
    public static void Dispose()
    {
        lock (_lock)
        {
            try
            {
                _writer?.Flush();
                _writer?.Dispose();
                _writer = null;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Logger] Dispose failed: {ex.Message}");
            }
        }
    }

    /// <summary>Delete log files older than retentionDays.</summary>
    private static void RotateLogs(string logDir, int retentionDays)
    {
        if (retentionDays <= 0) return;
        try
        {
            var cutoff = DateTime.Now.AddDays(-retentionDays);
            foreach (var file in Directory.EnumerateFiles(logDir, "zapret_*.log"))
            {
                var fi = new FileInfo(file);
                if (fi.LastWriteTime < cutoff)
                    fi.Delete();
            }
        }
        catch (Exception ex)
        {
            // Non-critical: rotation failure should not crash the app
            Console.Error.WriteLine($"[Logger] Log rotation failed: {ex.Message}");
        }
    }
}
