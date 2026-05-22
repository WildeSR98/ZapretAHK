using System.Net.NetworkInformation;
using System.Text.Json;
using ZapretManager.Core;
using ZapretManager.UI;

namespace ZapretManager.Service;

/// <summary>
/// Network interface selector — choose which NIC to use for winws --iface.
/// </summary>
public static class NicSelector
{
    private const string ConfigFile = "selected_nic.json";

    public record NicInfo(string Id, string Name, string Type, string Ip, string Speed, string Status);

    public static void Run(string rootDir)
    {
        Console.Clear();
        ConsoleMenu.WriteHeader("ВЫБОР СЕТЕВОГО АДАПТЕРА");
        Console.WriteLine();

        var nics = GetAvailableNics();
        if (nics.Count == 0)
        {
            ConsoleMenu.WriteWarn("Нет доступных сетевых адаптеров");
            ConsoleMenu.PauseAny();
            return;
        }

        var selectedId = LoadSelection(rootDir);

        for (int i = 0; i < nics.Count; i++)
        {
            var n = nics[i];
            var marker = n.Id == selectedId ? " ✓" : "";
            Console.ForegroundColor = n.Status == "Up" ? ConsoleColor.Green : ConsoleColor.DarkGray;
            Console.WriteLine($"   {i + 1}. {n.Name}{marker}");
            Console.ResetColor();
            Console.WriteLine($"      Тип: {n.Type}  |  IP: {n.Ip}  |  Скорость: {n.Speed}  |  {n.Status}");
        }

        Console.WriteLine();
        Console.WriteLine($"   0. Авто (без привязки)");
        Console.WriteLine();

        var input = ConsoleMenu.Prompt("Выберите адаптер", "0");
        if (input == "0")
        {
            ClearSelection(rootDir);
            ConsoleMenu.WriteOk("Адаптер: авто");
        }
        else if (int.TryParse(input, out var idx) && idx > 0 && idx <= nics.Count)
        {
            SaveSelection(rootDir, nics[idx - 1]);
            ConsoleMenu.WriteOk($"Выбран: {nics[idx - 1].Name}");
            ConsoleMenu.WriteInfo("При следующей установке службы будет использован параметр --iface");
        }

        ConsoleMenu.PauseAny();
    }

    public static List<NicInfo> GetAvailableNics()
    {
        var result = new List<NicInfo>();
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var ni in interfaces)
            {
                // Skip loopback, tunnel, and down interfaces
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                if (ni.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                    !ni.Description.Contains("VPN", StringComparison.OrdinalIgnoreCase)) continue;

                var props = ni.GetIPProperties();
                var ipv4 = props.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                var ip = ipv4?.Address.ToString() ?? "—";

                var speed = ni.Speed > 0 ? $"{ni.Speed / 1_000_000} Mbps" : "—";

                var type = ni.NetworkInterfaceType switch
                {
                    NetworkInterfaceType.Ethernet => "Ethernet",
                    NetworkInterfaceType.Wireless80211 => "WiFi",
                    _ => ni.NetworkInterfaceType.ToString()
                };

                result.Add(new NicInfo(ni.Id, ni.Name, type, ip, speed, ni.OperationalStatus.ToString()));
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"NIC enumeration failed: {ex.Message}");
        }

        return result;
    }

    public static string? GetSelectedNicName(string rootDir)
    {
        try
        {
            var path = Path.Combine(rootDir, "utils", ConfigFile);
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("name").GetString();
        }
        catch (Exception ex) { Logger.Error($"[NicSelector] {ex.GetType().Name}: {ex.Message}"); return null; }
    }

    private static string? LoadSelection(string rootDir)
    {
        try
        {
            var path = Path.Combine(rootDir, "utils", ConfigFile);
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("id").GetString();
        }
        catch (Exception ex) { Logger.Error($"[NicSelector] {ex.GetType().Name}: {ex.Message}"); return null; }
    }

    private static void SaveSelection(string rootDir, NicInfo nic)
    {
        try
        {
            var path = Path.Combine(rootDir, "utils", ConfigFile);
            var json = JsonSerializer.Serialize(new { id = nic.Id, name = nic.Name, type = nic.Type },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex) { Logger.Warn($"NIC save failed: {ex.Message}"); }
    }

    private static void ClearSelection(string rootDir)
    {
        try
        {
            var path = Path.Combine(rootDir, "utils", ConfigFile);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) { Logger.Error($"[NicSelector] {ex.GetType().Name}: {ex.Message}"); }
    }
}
