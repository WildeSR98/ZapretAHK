using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZapretManager.Service;

/// <summary>
/// Settings for TG WS Proxy — mirrors TgProxyLauncher ProxySettings.
/// Saved to tg-proxy-settings.json in RootDir.
/// </summary>
public class TgProxySettings
{
    [JsonPropertyName("host")]
    public string Host { get; set; } = "127.0.0.1";

    [JsonPropertyName("port")]
    public int Port { get; set; } = 1443;

    [JsonPropertyName("secret")]
    public string Secret { get; set; } = "";

    [JsonPropertyName("fakeTlsDomain")]
    public string FakeTlsDomain { get; set; } = "";

    [JsonPropertyName("cfProxyEnabled")]
    public bool CfProxyEnabled { get; set; } = true;

    [JsonPropertyName("cfProxyDomain")]
    public string CfProxyDomain { get; set; } = "";

    [JsonPropertyName("poolSize")]
    public int PoolSize { get; set; } = 4;

    [JsonPropertyName("bufKb")]
    public int BufKb { get; set; } = 256;

    [JsonPropertyName("logMaxMb")]
    public int LogMaxMb { get; set; } = 5;

    [JsonPropertyName("verbose")]
    public bool Verbose { get; set; } = false;

    [JsonPropertyName("dcIps")]
    public List<string> DcIps { get; set; } = new() { "2:149.154.167.220", "4:149.154.167.220" };
}

/// <summary>
/// Manages TgWsProxy_windows.exe — a native PyInstaller-bundled TG WS Proxy.
/// Accepts the same arguments as the Python tg_ws_proxy.py script.
/// </summary>
public static class TgProxyManager
{
    private const string ExeName = "TgWsProxy_windows.exe";
    private const string ProcessName = "TgWsProxy_windows";
    private const string SettingsFileName = "tg-proxy-settings.json";

    /// <summary>Check if TgWsProxy_windows process is running.</summary>
    public static bool IsRunning()
        => Process.GetProcessesByName(ProcessName).Length > 0;

    /// <summary>
    /// Find TgWsProxy_windows.exe path. Looks in RootDir first, then orig/.
    /// </summary>
    public static string? FindExePath(string rootDir)
    {
        var rootPath = Path.Combine(rootDir, ExeName);
        if (File.Exists(rootPath)) return rootPath;

        var origPath = Path.Combine(rootDir, "orig", ExeName);
        if (File.Exists(origPath)) return origPath;

        return null;
    }

    /// <summary>
    /// Copies TgWsProxy_windows.exe from orig/ to RootDir if not already there.
    /// Returns the final path.
    /// </summary>
    public static string? EnsureInRootDir(string rootDir)
    {
        var rootPath = Path.Combine(rootDir, ExeName);
        if (File.Exists(rootPath)) return rootPath;

        // Search locations: orig/, parent dir (project root)
        var searchPaths = new[]
        {
            Path.Combine(rootDir, "orig", ExeName),
            Path.Combine(rootDir, "..", ExeName),
            Path.Combine(rootDir, "..", "orig", ExeName),
        };

        foreach (var src in searchPaths)
        {
            if (File.Exists(src))
            {
                try
                {
                    File.Copy(src, rootPath, overwrite: false);
                    Core.Logger.Info($"Copied {ExeName} from {Path.GetDirectoryName(src)} to {rootDir}");
                    return rootPath;
                }
                catch (Exception ex)
                {
                    Core.Logger.Warn($"Failed to copy {ExeName}: {ex.Message}");
                }
            }
        }

        return null;
    }

    /// <summary>Start TgWsProxy with the given settings.</summary>
    public static Process? Start(string rootDir, TgProxySettings settings)
    {
        var exePath = Path.Combine(rootDir, ExeName);
        if (!File.Exists(exePath))
        {
            Core.Logger.Error($"{ExeName} not found at {exePath}");
            return null;
        }

        // Build arguments list (same as TgProxyLauncher)
        var args = new List<string>
        {
            $"--host {settings.Host}",
            $"--port {settings.Port}",
            $"--secret {settings.Secret}",
            $"--buf-kb {settings.BufKb}",
            $"--pool-size {settings.PoolSize}",
        };

        if (!string.IsNullOrEmpty(settings.FakeTlsDomain))
            args.Add($"--fake-tls-domain {settings.FakeTlsDomain}");

        if (!settings.CfProxyEnabled)
            args.Add("--no-cfproxy");

        if (!string.IsNullOrEmpty(settings.CfProxyDomain))
            args.Add($"--cfproxy-domain {settings.CfProxyDomain}");

        foreach (var dcIp in settings.DcIps)
            args.Add($"--dc-ip {dcIp}");

        if (settings.Verbose)
            args.Add("-v");

        var logFile = Path.Combine(rootDir, "logs", "tg-proxy.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
        args.Add($"--log-file \"{logFile}\"");
        args.Add($"--log-max-mb {settings.LogMaxMb}");

        var allArgs = string.Join(" ", args);
        Core.Logger.Info($"Starting {ExeName} with args: {allArgs}");

        try
        {
            var proc = Process.Start(new ProcessStartInfo(exePath, allArgs)
            {
                WorkingDirectory = rootDir,
                UseShellExecute = false,
                CreateNoWindow = false,
            });
            return proc;
        }
        catch (Exception ex)
        {
            Core.Logger.Error($"Failed to start {ExeName}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Stop all TgWsProxy_windows processes.</summary>
    public static void Stop()
    {
        foreach (var p in Process.GetProcessesByName(ProcessName))
        {
            try
            {
                p.Kill(entireProcessTree: true);
                Core.Logger.Info($"Killed {ProcessName} (PID: {p.Id})");
            }
            catch { }
        }
    }

    /// <summary>Generate tg:// proxy link for Telegram.</summary>
    public static string GenerateLink(TgProxySettings settings)
    {
        if (!string.IsNullOrEmpty(settings.FakeTlsDomain))
        {
            var domainHex = BitConverter.ToString(
                Encoding.ASCII.GetBytes(settings.FakeTlsDomain))
                .Replace("-", "").ToLower();
            return $"tg://proxy?server={settings.Host}&port={settings.Port}" +
                   $"&secret=ee{settings.Secret}{domainHex}";
        }
        return $"tg://proxy?server={settings.Host}&port={settings.Port}" +
               $"&secret=dd{settings.Secret}";
    }

    /// <summary>Load settings from tg-proxy-settings.json.</summary>
    public static TgProxySettings LoadSettings(string rootDir)
    {
        var path = Path.Combine(rootDir, SettingsFileName);
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<TgProxySettings>(json);
                if (settings != null)
                {
                    // Generate secret if empty
                    if (string.IsNullOrEmpty(settings.Secret))
                    {
                        settings.Secret = Guid.NewGuid().ToString("N")[..32];
                        SaveSettings(rootDir, settings);
                    }
                    return settings;
                }
            }
            catch { }
        }

        // Default settings with generated secret
        var defaults = new TgProxySettings
        {
            Secret = Guid.NewGuid().ToString("N")[..32]
        };
        SaveSettings(rootDir, defaults);
        return defaults;
    }

    /// <summary>Save settings to tg-proxy-settings.json.</summary>
    public static void SaveSettings(string rootDir, TgProxySettings settings)
    {
        var path = Path.Combine(rootDir, SettingsFileName);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(path, json, Encoding.UTF8);
    }
}
