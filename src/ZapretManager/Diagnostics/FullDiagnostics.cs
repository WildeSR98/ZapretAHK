using System.Diagnostics;
using System.ServiceProcess;
using Microsoft.Win32;
using ZapretManager.Core;

namespace ZapretManager.Diagnostics;

public record DiagResult(string Name, DiagLevel Level, string Message, string? HelpUrl = null);

public enum DiagLevel { Ok, Warning, Error }

/// <summary>
/// Full diagnostics matching all checks from the original service.bat (lines 406-704).
/// </summary>
public static class FullDiagnostics
{
    public static async Task<List<DiagResult>> RunAllAsync(string binDir)
    {
        var results = new List<DiagResult>();

        results.Add(CheckBfe());
        results.Add(CheckSystemProxy());
        results.Add(CheckTcpTimestamps());
        results.Add(CheckAdguard());
        results.Add(CheckKillerServices());
        results.Add(CheckIntelConnectivity());
        results.Add(CheckCheckPoint());
        results.Add(CheckSmartByte());
        results.Add(CheckWinDivertSys(binDir));
        results.Add(CheckVpnServices());
        results.Add(CheckSecureDns());
        results.Add(CheckHostsFile());
        results.Add(CheckWinDivertConflict());

        await Task.CompletedTask;
        return results;
    }

    /// <summary>Base Filtering Engine — required for zapret to work.</summary>
    static DiagResult CheckBfe()
    {
        try
        {
            using var sc = new ServiceController("BFE");
            if (sc.Status == ServiceControllerStatus.Running)
                return new("BFE", DiagLevel.Ok, "Base Filtering Engine check passed");
            return new("BFE", DiagLevel.Error,
                "[X] Base Filtering Engine is not running. This service is required for zapret to work");
        }
        catch
        {
            return new("BFE", DiagLevel.Error,
                "[X] Base Filtering Engine service not found");
        }
    }

    /// <summary>System proxy check via registry.</summary>
    static DiagResult CheckSystemProxy()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            if (key == null)
                return new("Proxy", DiagLevel.Ok, "Proxy check passed");

            var proxyEnable = key.GetValue("ProxyEnable");
            if (proxyEnable is int enabled && enabled == 1)
            {
                var proxyServer = key.GetValue("ProxyServer")?.ToString() ?? "unknown";
                return new("Proxy", DiagLevel.Warning,
                    $"[?] System proxy is enabled: {proxyServer}. Make sure it's valid or disable it if you don't use a proxy");
            }
            return new("Proxy", DiagLevel.Ok, "Proxy check passed");
        }
        catch
        {
            return new("Proxy", DiagLevel.Ok, "Proxy check passed (unable to read registry)");
        }
    }

    /// <summary>TCP timestamps check and auto-enable.</summary>
    static DiagResult CheckTcpTimestamps()
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", "interface tcp show global")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var proc = Process.Start(psi);
            if (proc == null)
                return new("TCP Timestamps", DiagLevel.Warning, "[?] Unable to check TCP timestamps");

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);

            if (output.Contains("enabled", StringComparison.OrdinalIgnoreCase) &&
                output.Contains("timestamps", StringComparison.OrdinalIgnoreCase))
            {
                // Check if the line with "timestamps" also has "enabled"
                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Contains("timestamps", StringComparison.OrdinalIgnoreCase))
                    {
                        if (line.Contains("enabled", StringComparison.OrdinalIgnoreCase))
                            return new("TCP Timestamps", DiagLevel.Ok, "TCP timestamps check passed");
                        break;
                    }
                }
            }

            // Try to enable
            var enablePsi = new ProcessStartInfo("netsh", "interface tcp set global timestamps=enabled")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            var enableProc = Process.Start(enablePsi);
            enableProc?.WaitForExit(5000);

            if (enableProc?.ExitCode == 0)
                return new("TCP Timestamps", DiagLevel.Ok, "TCP timestamps successfully enabled");
            return new("TCP Timestamps", DiagLevel.Error, "[X] Failed to enable TCP timestamps");
        }
        catch
        {
            return new("TCP Timestamps", DiagLevel.Warning, "[?] Unable to check TCP timestamps");
        }
    }

    /// <summary>Adguard process check.</summary>
    static DiagResult CheckAdguard()
    {
        var procs = Process.GetProcessesByName("AdguardSvc");
        if (procs.Length > 0)
            return new("Adguard", DiagLevel.Error,
                "[X] Adguard process found. Adguard may cause problems with Discord",
                "https://github.com/Flowseal/zapret-discord-youtube/issues/417");
        return new("Adguard", DiagLevel.Ok, "Adguard check passed");
    }

    /// <summary>Killer services check (Intel Killer Network).</summary>
    static DiagResult CheckKillerServices()
    {
        try
        {
            var services = ServiceController.GetServices();
            foreach (var svc in services)
            {
                if (svc.ServiceName.Contains("Killer", StringComparison.OrdinalIgnoreCase) ||
                    svc.DisplayName.Contains("Killer", StringComparison.OrdinalIgnoreCase))
                {
                    return new("Killer", DiagLevel.Error,
                        "[X] Killer services found. Killer conflicts with zapret",
                        "https://github.com/Flowseal/zapret-discord-youtube/issues/2512#issuecomment-2821119513");
                }
            }
            return new("Killer", DiagLevel.Ok, "Killer check passed");
        }
        catch
        {
            return new("Killer", DiagLevel.Ok, "Killer check passed (unable to query services)");
        }
    }

    /// <summary>Intel Connectivity Network Service check.</summary>
    static DiagResult CheckIntelConnectivity()
    {
        try
        {
            var services = ServiceController.GetServices();
            foreach (var svc in services)
            {
                var name = svc.DisplayName;
                if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase) &&
                    name.Contains("Connectivity", StringComparison.OrdinalIgnoreCase) &&
                    name.Contains("Network", StringComparison.OrdinalIgnoreCase))
                {
                    return new("Intel Connectivity", DiagLevel.Error,
                        "[X] Intel Connectivity Network Service found. It conflicts with zapret",
                        "https://github.com/ValdikSS/GoodbyeDPI/issues/541#issuecomment-2661670982");
                }
            }
            return new("Intel Connectivity", DiagLevel.Ok, "Intel Connectivity check passed");
        }
        catch
        {
            return new("Intel Connectivity", DiagLevel.Ok, "Intel Connectivity check passed");
        }
    }

    /// <summary>Check Point services (TracSrvWrapper, EPWD).</summary>
    static DiagResult CheckCheckPoint()
    {
        try
        {
            var services = ServiceController.GetServices();
            bool found = false;
            foreach (var svc in services)
            {
                if (svc.ServiceName.Equals("TracSrvWrapper", StringComparison.OrdinalIgnoreCase) ||
                    svc.ServiceName.Equals("EPWD", StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    break;
                }
            }

            if (found)
                return new("Check Point", DiagLevel.Error,
                    "[X] Check Point services found. Check Point conflicts with zapret. Try to uninstall Check Point");
            return new("Check Point", DiagLevel.Ok, "Check Point check passed");
        }
        catch
        {
            return new("Check Point", DiagLevel.Ok, "Check Point check passed");
        }
    }

    /// <summary>SmartByte services check.</summary>
    static DiagResult CheckSmartByte()
    {
        try
        {
            var services = ServiceController.GetServices();
            foreach (var svc in services)
            {
                if (svc.ServiceName.Contains("SmartByte", StringComparison.OrdinalIgnoreCase) ||
                    svc.DisplayName.Contains("SmartByte", StringComparison.OrdinalIgnoreCase))
                {
                    return new("SmartByte", DiagLevel.Error,
                        "[X] SmartByte services found. SmartByte conflicts with zapret. Try to uninstall or disable through services.msc");
                }
            }
            return new("SmartByte", DiagLevel.Ok, "SmartByte check passed");
        }
        catch
        {
            return new("SmartByte", DiagLevel.Ok, "SmartByte check passed");
        }
    }

    /// <summary>WinDivert64.sys file existence check.</summary>
    static DiagResult CheckWinDivertSys(string binDir)
    {
        var sysFiles = Directory.Exists(binDir)
            ? Directory.GetFiles(binDir, "*.sys")
            : Array.Empty<string>();

        if (sysFiles.Length == 0)
            return new("WinDivert64.sys", DiagLevel.Error,
                "[X] WinDivert64.sys file NOT found in bin/");
        return new("WinDivert64.sys", DiagLevel.Ok, "WinDivert64.sys found");
    }

    /// <summary>VPN services check.</summary>
    static DiagResult CheckVpnServices()
    {
        try
        {
            var services = ServiceController.GetServices();
            var vpnServices = new List<string>();
            foreach (var svc in services)
            {
                if (svc.ServiceName.Contains("VPN", StringComparison.OrdinalIgnoreCase) ||
                    svc.DisplayName.Contains("VPN", StringComparison.OrdinalIgnoreCase))
                {
                    vpnServices.Add(svc.ServiceName);
                }
            }

            if (vpnServices.Count > 0)
                return new("VPN", DiagLevel.Warning,
                    $"[?] VPN services found: {string.Join(", ", vpnServices)}. Some VPNs can conflict with zapret. Make sure all VPNs are disabled");
            return new("VPN", DiagLevel.Ok, "VPN check passed");
        }
        catch
        {
            return new("VPN", DiagLevel.Ok, "VPN check passed");
        }
    }

    /// <summary>Secure DNS (DoH) check via registry.</summary>
    static DiagResult CheckSecureDns()
    {
        try
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(
                @"System\CurrentControlSet\Services\Dnscache\InterfaceSpecificParameters");
            if (baseKey == null)
            {
                return new("Secure DNS", DiagLevel.Warning,
                    "[?] Make sure you have configured secure DNS in a browser with some non-default DNS service provider");
            }

            bool dohFound = false;
            foreach (var subKeyName in baseKey.GetSubKeyNames())
            {
                using var subKey = baseKey.OpenSubKey(subKeyName);
                if (subKey == null) continue;

                foreach (var innerName in subKey.GetSubKeyNames())
                {
                    using var innerKey = subKey.OpenSubKey(innerName);
                    if (innerKey == null) continue;

                    var dohFlags = innerKey.GetValue("DohFlags");
                    if (dohFlags is int flags && flags > 0)
                    {
                        dohFound = true;
                        break;
                    }
                }
                if (dohFound) break;
            }

            if (dohFound)
                return new("Secure DNS", DiagLevel.Ok, "Secure DNS check passed");

            return new("Secure DNS", DiagLevel.Warning,
                "[?] Make sure you have configured secure DNS in a browser with some non-default DNS service provider. " +
                "If you use Windows 11, you can configure encrypted DNS in Settings to hide this warning");
        }
        catch
        {
            return new("Secure DNS", DiagLevel.Warning,
                "[?] Unable to check Secure DNS configuration");
        }
    }

    /// <summary>Hosts file check for youtube.com / youtu.be entries.</summary>
    static DiagResult CheckHostsFile()
    {
        try
        {
            var hostsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32", "drivers", "etc", "hosts");

            if (!File.Exists(hostsPath))
                return new("Hosts file", DiagLevel.Ok, "Hosts file check passed");

            var content = File.ReadAllText(hostsPath);
            bool ytFound = content.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                           content.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);

            if (ytFound)
                return new("Hosts file", DiagLevel.Warning,
                    "[?] Your hosts file contains entries for youtube.com or youtu.be. This may cause problems with YouTube access");
            return new("Hosts file", DiagLevel.Ok, "Hosts file check passed");
        }
        catch
        {
            return new("Hosts file", DiagLevel.Ok, "Hosts file check passed (unable to read)");
        }
    }

    /// <summary>
    /// WinDivert conflict: winws.exe is not running but WinDivert service is active.
    /// Attempts to clean up if detected.
    /// </summary>
    static DiagResult CheckWinDivertConflict()
    {
        try
        {
            bool winwsRunning = Process.GetProcessesByName("winws").Length > 0;

            bool windivertActive = false;
            try
            {
                using var sc = new ServiceController("WinDivert");
                windivertActive = sc.Status == ServiceControllerStatus.Running ||
                                  sc.Status == ServiceControllerStatus.StopPending;
            }
            catch (Exception ex) { Logger.Error($"[FullDiagnostics] {ex.GetType().Name}: {ex.Message}"); /* service not found — OK */ }

            if (!winwsRunning && windivertActive)
            {
                // Attempt to clean up
                Service.WinServiceManager.Stop("WinDivert");
                Service.WinServiceManager.Remove("WinDivert");

                // Check again
                bool stillActive = false;
                try
                {
                    using var sc = new ServiceController("WinDivert");
                    _ = sc.Status;
                    stillActive = true;
                }
                catch (Exception ex) { Logger.Error($"[FullDiagnostics] {ex.GetType().Name}: {ex.Message}"); }

                if (stillActive)
                    return new("WinDivert Conflict", DiagLevel.Error,
                        "[X] winws.exe is not running but WinDivert service is still active. Check manually if any other bypass is using WinDivert");

                return new("WinDivert Conflict", DiagLevel.Ok,
                    "WinDivert successfully removed (was active without winws.exe)");
            }

            return new("WinDivert Conflict", DiagLevel.Ok, "WinDivert conflict check passed");
        }
        catch
        {
            return new("WinDivert Conflict", DiagLevel.Ok, "WinDivert conflict check passed");
        }
    }
}
