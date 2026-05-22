using System.Net.Http;
using System.Text.Json;
using ZapretManager.Core;
using ZapretManager.UI;

namespace ZapretManager.Diagnostics;

/// <summary>
/// Detects ISP/provider by IP and recommends strategies.
/// Uses ipinfo.io / ip-api.com APIs.
/// </summary>
public static class IspDetector
{
    public record IspInfo(string Ip, string Isp, string Org, string City, string Region, string Country, string As);

    private const string CacheFile = "isp_cache.json";
    private const string StrategiesMapFile = "isp_strategies.json";
    private const string StrategiesMapUrl = "https://raw.githubusercontent.com/WildeSR98/12345/main/utils/isp_strategies.json";

    public static async Task<IspInfo?> DetectAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            // Try ip-api.com first (no key needed, 45 req/min)
            try
            {
                var json = await http.GetStringAsync("http://ip-api.com/json/?fields=query,isp,org,city,regionName,country,as");
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                return new IspInfo(
                    root.GetProperty("query").GetString() ?? "",
                    root.GetProperty("isp").GetString() ?? "",
                    root.GetProperty("org").GetString() ?? "",
                    root.GetProperty("city").GetString() ?? "",
                    root.GetProperty("regionName").GetString() ?? "",
                    root.GetProperty("country").GetString() ?? "",
                    root.GetProperty("as").GetString() ?? ""
                );
            }
            catch
            {
                // Fallback to ipinfo.io
                var json = await http.GetStringAsync("https://ipinfo.io/json");
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                return new IspInfo(
                    root.GetProperty("ip").GetString() ?? "",
                    root.TryGetProperty("org", out var org) ? org.GetString() ?? "" : "",
                    root.TryGetProperty("org", out var org2) ? org2.GetString() ?? "" : "",
                    root.TryGetProperty("city", out var city) ? city.GetString() ?? "" : "",
                    root.TryGetProperty("region", out var reg) ? reg.GetString() ?? "" : "",
                    root.TryGetProperty("country", out var co) ? co.GetString() ?? "" : "",
                    ""
                );
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"ISP detection failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Cache ISP info to disk.</summary>
    public static void SaveCache(string rootDir, IspInfo info)
    {
        try
        {
            var path = Path.Combine(rootDir, "utils", CacheFile);
            var json = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex) { Logger.Error($"[IspDetector] {ex.GetType().Name}: {ex.Message}"); }
    }

    /// <summary>Load cached ISP info.</summary>
    public static IspInfo? LoadCache(string rootDir)
    {
        try
        {
            var path = Path.Combine(rootDir, "utils", CacheFile);
            if (!File.Exists(path)) return null;

            // Refresh if older than 24h
            if (File.GetLastWriteTime(path) < DateTime.Now.AddHours(-24)) return null;

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<IspInfo>(json);
        }
        catch (Exception ex) { Logger.Error($"[IspDetector] {ex.GetType().Name}: {ex.Message}"); return null; }
    }

    /// <summary>Get recommended strategies for ISP.</summary>
    public static async Task<List<string>> GetRecommendationsAsync(string rootDir, string isp)
    {
        var map = await LoadStrategiesMapAsync(rootDir);
        if (map.Count == 0) return new();

        // Try exact match first
        foreach (var kv in map)
        {
            if (isp.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        }

        // Fallback to default
        return map.GetValueOrDefault("default", new());
    }

    /// <summary>Load ISP→strategies map, downloading and merging if needed.</summary>
    private static async Task<Dictionary<string, List<string>>> LoadStrategiesMapAsync(string rootDir)
    {
        var localPath = Path.Combine(rootDir, "utils", StrategiesMapFile);
        var localMap = new Dictionary<string, List<string>>();

        // Load existing local map
        if (File.Exists(localPath))
        {
            try
            {
                var json = File.ReadAllText(localPath);
                localMap = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json) ?? new();
            }
            catch (Exception ex) { Logger.Error($"[IspDetector] {ex.GetType().Name}: {ex.Message}"); }
        }

        // Try to download and merge remote map
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var remoteJson = await http.GetStringAsync(StrategiesMapUrl);
            var remoteMap = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(remoteJson);

            if (remoteMap != null)
            {
                // Merge: remote entries + local-only entries
                foreach (var kv in remoteMap)
                {
                    if (!localMap.ContainsKey(kv.Key))
                        localMap[kv.Key] = kv.Value;
                    else
                    {
                        // Merge lists — add remote entries not in local
                        foreach (var strat in kv.Value)
                            if (!localMap[kv.Key].Contains(strat))
                                localMap[kv.Key].Add(strat);
                    }
                }

                // Save merged
                var merged = JsonSerializer.Serialize(localMap, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(localPath, merged);
            }
        }
        catch (Exception ex) { Logger.Error($"[IspDetector] {ex.GetType().Name}: {ex.Message}"); /* offline — use local only */ }

        return localMap;
    }

    public static void Print(IspInfo info)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("   ── ИНФОРМАЦИЯ О ПРОВАЙДЕРЕ ──");
        Console.ResetColor();
        Console.WriteLine($"   IP:       {info.Ip}");
        Console.WriteLine($"   ISP:      {info.Isp}");
        Console.WriteLine($"   Org:      {info.Org}");
        Console.WriteLine($"   AS:       {info.As}");
        Console.WriteLine($"   Город:    {info.City}");
        Console.WriteLine($"   Регион:   {info.Region}");
        Console.WriteLine($"   Страна:   {info.Country}");
    }
}
