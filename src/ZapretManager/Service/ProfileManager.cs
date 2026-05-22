using System.Text.Json;
using System.Text.Json.Serialization;
using ZapretManager.Core;
using ZapretManager.UI;

namespace ZapretManager.Service;

/// <summary>
/// Strategy profiles: save/load/switch combinations of strategy + game filter + ipset + update mode.
/// </summary>
public static class ProfileManager
{
    private const string ProfilesDir = "profiles";

    public class Profile
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("strategy")] public string Strategy { get; set; } = "";
        [JsonPropertyName("game_filter")] public string GameFilter { get; set; } = "disabled";
        [JsonPropertyName("ipset_mode")] public string IpsetMode { get; set; } = "any";
        [JsonPropertyName("update_mode")] public string UpdateMode { get; set; } = "manual";
        [JsonPropertyName("created_at")] public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    /// <summary>Save current configuration as a named profile.</summary>
    public static string? SaveProfile(string rootDir, string name)
    {
        try
        {
            var profilesPath = Path.Combine(rootDir, ProfilesDir);
            Directory.CreateDirectory(profilesPath);

            var profile = new Profile
            {
                Name = name,
                Strategy = GetCurrentStrategyName(rootDir),
                GameFilter = Service.GameFilter.StatusLabel(Path.Combine(rootDir, "utils")),
                IpsetMode = GetIpsetMode(rootDir),
                UpdateMode = Updates.UpdateChecker.GetUpdateMode(rootDir),
                CreatedAt = DateTime.Now
            };

            var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
            var fileName = SanitizeFileName(name) + ".json";
            var path = Path.Combine(profilesPath, fileName);
            File.WriteAllText(path, json);
            Logger.Ok($"Профиль сохранён: {name} → {fileName}");
            return path;
        }
        catch (Exception ex)
        {
            Logger.Error($"Ошибка сохранения профиля: {ex.Message}");
            return null;
        }
    }

    /// <summary>Load a profile from file.</summary>
    public static Profile? LoadProfile(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<Profile>(json);
        }
        catch (Exception ex)
        {
            Logger.Error($"Ошибка чтения профиля: {ex.Message}");
            return null;
        }
    }

    /// <summary>Apply a profile: set strategy, game filter, ipset.</summary>
    public static bool ApplyProfile(Profile profile, string rootDir, string binDir, string listsDir, string utilsDir)
    {
        try
        {
            // Set Game Filter
            Service.GameFilter.Set(utilsDir, profile.GameFilter);
            Logger.Info($"Game Filter → {profile.GameFilter}");

            // Set IPSet mode
            SetIpsetMode(rootDir, listsDir, profile.IpsetMode);
            Logger.Info($"IPSet → {profile.IpsetMode}");

            // Set update mode
            Updates.UpdateChecker.SetUpdateMode(rootDir, profile.UpdateMode);
            Logger.Info($"Update mode → {profile.UpdateMode}");

            // Install strategy
            var strategyFile = FindStrategyFile(rootDir, profile.Strategy);
            if (strategyFile != null)
            {
                var gf = Service.GameFilter.Get(utilsDir);
                var args = StrategyReader.ParseArgs(strategyFile, binDir, listsDir, gf.Tcp, gf.Udp);
                var winws = Path.Combine(binDir, "winws.exe");
                WinServiceManager.Install("zapret", "zapret", "Zapret DPI bypass", $"\"{winws}\" {args}");
                Logger.Ok($"Стратегия установлена: {profile.Strategy}");
            }
            else
            {
                ConsoleMenu.WriteWarn($"Стратегия '{profile.Strategy}' не найдена");
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Ошибка применения профиля: {ex}");
            return false;
        }
    }

    /// <summary>List saved profiles.</summary>
    public static Profile[] ListProfiles(string rootDir)
    {
        var profilesPath = Path.Combine(rootDir, ProfilesDir);
        if (!Directory.Exists(profilesPath)) return Array.Empty<Profile>();

        return Directory.GetFiles(profilesPath, "*.json")
            .Select(f => LoadProfile(f))
            .Where(p => p != null)
            .Cast<Profile>()
            .OrderBy(p => p.Name)
            .ToArray();
    }

    /// <summary>Delete a profile by name.</summary>
    public static bool DeleteProfile(string rootDir, string name)
    {
        var profilesPath = Path.Combine(rootDir, ProfilesDir);
        var fileName = SanitizeFileName(name) + ".json";
        var path = Path.Combine(profilesPath, fileName);
        if (File.Exists(path)) { File.Delete(path); return true; }
        return false;
    }

    // ── Helpers ──

    private static string GetCurrentStrategyName(string rootDir)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"System\CurrentControlSet\Services\zapret");
            return key?.GetValue("zapret-discord-youtube")?.ToString() ?? "unknown";
        }
        catch (Exception ex) { Logger.Error($"[ProfileManager] {ex.GetType().Name}: {ex.Message}"); return "unknown"; }
    }

    private static string GetIpsetMode(string rootDir)
    {
        var f = Path.Combine(rootDir, "lists", "ipset-all.txt");
        if (!File.Exists(f)) return "none";
        var lines = File.ReadAllLines(f).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        if (lines.Length == 0) return "any";
        return "loaded";
    }

    private static void SetIpsetMode(string rootDir, string listsDir, string mode)
    {
        var listFile = Path.Combine(listsDir, "ipset-all.txt");
        switch (mode)
        {
            case "any":
                if (File.Exists(listFile))
                {
                    var backup = listFile + ".backup";
                    if (!File.Exists(backup)) File.Copy(listFile, backup, true);
                }
                File.WriteAllText(listFile, "\r\n");
                break;
            case "none":
                File.WriteAllText(listFile, "203.0.113.113/32\r\n");
                break;
            // "loaded" — keep current file
        }
    }

    private static string? FindStrategyFile(string rootDir, string strategyName)
    {
        var strategiesDir = Path.Combine(rootDir, "strategies");
        if (!Directory.Exists(strategiesDir)) return null;
        var files = Directory.GetFiles(strategiesDir, "general*.bat");
        return files.FirstOrDefault(f =>
            Path.GetFileNameWithoutExtension(f).Equals(strategyName, StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(f).Contains(strategyName, StringComparison.OrdinalIgnoreCase));
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).ToLower();
    }
}
