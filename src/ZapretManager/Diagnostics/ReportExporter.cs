using System.ServiceProcess;
using ZapretManager.Core;

namespace ZapretManager.Diagnostics;

public static class ReportExporter
{
    public static string Export(string rootDir)
    {
        var logDir = Path.Combine(rootDir, "logs");
        Directory.CreateDirectory(logDir);
        var outPath = Path.Combine(logDir, $"diagnostics_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

        var lines = new List<string>
        {
            "ZAPRET DIAGNOSTICS REPORT",
            $"Generated: {DateTime.Now}",
            $".NET: {Environment.Version}  OS: {Environment.OSVersion}",
            new string('=', 60),
            "",
            "--- SERVICES ---"
        };

        foreach (var svc in new[] { "zapret", "WinDivert", "WinDivert14" })
        {
            var state = Service.WinServiceManager.GetState(svc);
            lines.Add($"{svc}: {state}");
        }

        lines.Add("");
        lines.Add("--- PROCESSES ---");
        var winws = System.Diagnostics.Process.GetProcessesByName("winws");
        lines.Add(winws.Length > 0
            ? $"winws.exe: RUNNING (PID={string.Join(",", winws.Select(p => p.Id))})"
            : "winws.exe: NOT RUNNING");

        lines.Add("");
        lines.Add("--- CONFLICTS ---");
        var conflicts = ConflictDetector.FindConflicts();
        lines.Add($"Found: {(conflicts.Count > 0 ? string.Join(", ", conflicts) : "none")}");

        lines.Add("");
        lines.Add("--- FILES ---");
        var binExe = Path.Combine(rootDir, "bin", "winws.exe");
        lines.Add(File.Exists(binExe)
            ? $"winws.exe: EXISTS ({new System.IO.FileInfo(binExe).Length} bytes)"
            : "winws.exe: MISSING");

        lines.Add("");
        lines.Add("--- STRATEGY (from registry) ---");
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"System\CurrentControlSet\Services\zapret");
            var strat = key?.GetValue("zapret-discord-youtube")?.ToString();
            lines.Add($"Installed: {strat ?? "none"}");
        }
        catch { lines.Add("Installed: unknown"); }

        lines.Add("");
        lines.Add("--- RECENT LOG ---");
        try
        {
            var logFiles = Directory.Exists(Path.Combine(rootDir, "logs"))
                ? new DirectoryInfo(Path.Combine(rootDir, "logs"))
                    .GetFiles("zapret_*.log")
                    .OrderByDescending(f => f.LastWriteTime)
                    .FirstOrDefault()
                : null;
            if (logFiles != null)
            {
                // Use FileShare.ReadWrite to read log file that may be locked by Logger
                using var fs = new FileStream(logFiles.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                var logLines = new List<string>();
                while (!sr.EndOfStream)
                {
                    var line = sr.ReadLine();
                    if (line != null) logLines.Add(line);
                }
                lines.AddRange(logLines.TakeLast(20));
            }
            else
                lines.Add("No logs found");
        }
        catch (Exception ex)
        {
            lines.Add($"Error reading log: {ex.Message}");
        }

        File.WriteAllLines(outPath, lines, new System.Text.UTF8Encoding(false));
        Logger.Ok($"Отчёт сохранён: {outPath}");

        // Also export JSON
        try { ExportJson(rootDir, outPath.Replace(".txt", ".json")); }
        catch (Exception ex) { Logger.Warn($"JSON экспорт не удался: {ex.Message}"); }

        return outPath;
    }

    /// <summary>Export structured JSON report for automated analysis.</summary>
    public static string ExportJson(string rootDir, string? outPath = null)
    {
        var logDir = Path.Combine(rootDir, "logs");
        Directory.CreateDirectory(logDir);
        outPath ??= Path.Combine(logDir, $"diagnostics_{DateTime.Now:yyyyMMdd_HHmmss}.json");

        var report = new Dictionary<string, object>
        {
            ["generated"] = DateTime.Now.ToString("O"),
            ["dotnet"] = Environment.Version.ToString(),
            ["os"] = Environment.OSVersion.ToString(),
            ["manager_version"] = ReadVersion(rootDir, "utils", "manager_version.txt"),
            ["core_version"] = ReadVersion(rootDir, "bin", "version.txt"),
        };

        // Services
        var services = new Dictionary<string, string>();
        foreach (var svc in new[] { "zapret", "WinDivert", "WinDivert14" })
            services[svc] = Service.WinServiceManager.GetState(svc).ToString();
        report["services"] = services;

        // Processes
        var procs = System.Diagnostics.Process.GetProcessesByName("winws");
        report["winws_running"] = procs.Length > 0;
        report["winws_pids"] = procs.Select(p => p.Id).ToList();

        // Strategy
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"System\CurrentControlSet\Services\zapret");
            report["strategy"] = key?.GetValue("zapret-discord-youtube")?.ToString() ?? "none";
            report["image_path"] = key?.GetValue("ImagePath")?.ToString() ?? "none";
        }
        catch { report["strategy"] = "unknown"; }

        // Conflicts
        report["conflicts"] = ConflictDetector.FindConflicts();

        // Files
        var files = new Dictionary<string, bool>();
        foreach (var f in new[] { "winws.exe", "WinDivert.dll", "WinDivert64.sys", "cygwin1.dll" })
            files[f] = File.Exists(Path.Combine(rootDir, "bin", f));
        report["files"] = files;

        var json = System.Text.Json.JsonSerializer.Serialize(report,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(outPath, json);
        Logger.Ok($"JSON отчёт: {outPath}");
        return outPath;
    }

    private static string ReadVersion(string rootDir, string subDir, string fileName)
    {
        var path = Path.Combine(rootDir, subDir, fileName);
        return File.Exists(path) ? File.ReadAllText(path).Trim() : "unknown";
    }
}
