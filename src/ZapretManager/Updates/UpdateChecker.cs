using System.Text.Json;
using System.Text.Json.Serialization;
using ZapretManager.Core;

namespace ZapretManager.Updates;

/// <summary>
/// Background update checker for GitHub releases.
/// Checks WildeSR98/12345 (manager) and Flowseal/zapret-discord-youtube (core).
/// </summary>
public static class UpdateChecker
{
    private static CancellationTokenSource? _cts;
    private static readonly object _lock = new();
    private const string CacheFileName = "update-check.json";
    private const string UpdateModeFileName = "update_mode.txt";

    /// <summary>Last check result (for menu header indicator).</summary>
    public static UpdateCheckResult? LastResult { get; private set; }

    /// <summary>Start background update checking loop.</summary>
    public static void StartBackground(AppConfig cfg, string rootDir)
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
        }

        var ct = _cts.Token;
        Task.Run(async () =>
        {
            bool firstRun = true;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // Check if updates are enabled
                    var utilsDir = Path.Combine(rootDir, "utils");
                    if (!File.Exists(Path.Combine(utilsDir, "check_updates.enabled")))
                    {
                        Logger.Info("Фоновая проверка обновлений отключена");
                        await Task.Delay(TimeSpan.FromHours(1), ct);
                        continue;
                    }

                    var result = await CheckNowAsync(cfg, rootDir);
                    LastResult = result;

                    // Show toast notifications
                    if (firstRun)
                    {
                        // First launch: always show toast (both statuses)
                        if (result.ManagerUpdateAvailable || result.CoreUpdateAvailable)
                        {
                            var parts = new List<string>();
                            if (result.ManagerUpdateAvailable)
                                parts.Add($"Manager v{result.ManagerRemote}");
                            if (result.CoreUpdateAvailable)
                                parts.Add($"Core {result.CoreRemote}");
                            UI.ToastNotifier.Show("Zapret — доступно обновление",
                                $"Новая версия: {string.Join(" | ", parts)}. Советуем обновить через меню п.9");

                            // Auto-update if enabled
                            if (GetUpdateMode(rootDir) == "auto")
                            {
                                Logger.Info("Автообновление: запуск...");
                                await RunAutoUpdateAsync(cfg, rootDir, result);
                            }
                        }
                        else
                        {
                            UI.ToastNotifier.Show("Zapret Manager",
                                "Обновления не требуются. Все версии актуальны.");
                        }
                        firstRun = false;
                    }
                    else
                    {
                        // Subsequent checks: toast ONLY if there are updates
                        if (result.ManagerUpdateAvailable || result.CoreUpdateAvailable)
                        {
                            var parts = new List<string>();
                            if (result.ManagerUpdateAvailable)
                                parts.Add($"Manager v{result.ManagerRemote}");
                            if (result.CoreUpdateAvailable)
                                parts.Add($"Core {result.CoreRemote}");
                            UI.ToastNotifier.Show("Zapret — доступно обновление",
                                $"Новая версия: {string.Join(" | ", parts)}. Советуем обновить через меню п.9");

                            if (GetUpdateMode(rootDir) == "auto")
                            {
                                Logger.Info("Автообновление: запуск...");
                                await RunAutoUpdateAsync(cfg, rootDir, result);
                            }
                        }
                    }

                    // Save cache
                    SaveCache(rootDir, result);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Logger.Warn($"Фоновая проверка обновлений: {ex.Message}");
                }

                // Wait interval
                var intervalHours = cfg.Features.UpdateCheckIntervalHours > 0
                    ? cfg.Features.UpdateCheckIntervalHours : 1;
                try { await Task.Delay(TimeSpan.FromHours(intervalHours), ct); }
                catch (OperationCanceledException) { break; }
            }
        }, ct);

        Logger.Info("Фоновая проверка обновлений запущена (интервал: каждый час)");
    }

    /// <summary>Stop background checking.</summary>
    public static void Stop()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts = null;
        }
    }

    /// <summary>Manual check (from menu item 9). Returns result without toast.</summary>
    public static async Task<UpdateCheckResult> CheckNowAsync(AppConfig cfg, string rootDir)
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("zapret-manager/2.0");
        http.Timeout = TimeSpan.FromSeconds(30);

        string? mgrRemote = null, mgrLocal = null, mgrDownloadUrl = null;
        string? coreRemote = null, coreLocal = null;

        // ── Check manager (WildeSR98/12345) ──
        try
        {
            var releaseApi = cfg.Repositories.Scripts12345.ReleaseApi;
            if (!string.IsNullOrWhiteSpace(releaseApi))
            {
                var req = new HttpRequestMessage(HttpMethod.Get, releaseApi);
                req.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
                    { NoCache = true };
                var resp = await http.SendAsync(req);

                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    mgrRemote = root.GetProperty("tag_name").GetString()?.TrimStart('v', 'V');

                    if (root.TryGetProperty("assets", out var assets))
                    {
                        foreach (var asset in assets.EnumerateArray())
                        {
                            var name = asset.GetProperty("name").GetString() ?? "";
                            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            {
                                mgrDownloadUrl = asset.GetProperty("browser_download_url").GetString();
                                break;
                            }
                        }
                    }
                    if (mgrDownloadUrl == null && root.TryGetProperty("zipball_url", out var zipball))
                        mgrDownloadUrl = zipball.GetString();
                }
            }
        }
        catch (Exception ex) { Logger.Warn($"Проверка обновлений manager: {ex.Message}"); }

        mgrLocal = GitHubUpdater.ReadManagerVersion(rootDir);

        // ── Check core (Flowseal/zapret-discord-youtube) ──
        try
        {
            var releaseApi = cfg.Repositories.ZapretCore.ReleaseApi;
            if (!string.IsNullOrWhiteSpace(releaseApi))
            {
                var req = new HttpRequestMessage(HttpMethod.Get, releaseApi);
                req.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
                    { NoCache = true };
                var resp = await http.SendAsync(req);

                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    var doc = JsonDocument.Parse(json);
                    coreRemote = doc.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v', 'V');
                }
            }
            else
            {
                // Fallback: version.txt from raw
                var versionUrl = cfg.Repositories.ZapretCore.VersionUrl;
                if (!string.IsNullOrWhiteSpace(versionUrl))
                {
                    var resp = await http.GetAsync(versionUrl);
                    if (resp.IsSuccessStatusCode)
                        coreRemote = (await resp.Content.ReadAsStringAsync()).Trim();
                }
            }
        }
        catch (Exception ex) { Logger.Warn($"Проверка обновлений core: {ex.Message}"); }

        coreLocal = ReadLocalCoreVersion(rootDir);

        var mgrUpdate = IsNewerVersion(mgrRemote, mgrLocal);
        var coreUpdate = IsNewerVersion(coreRemote, coreLocal);

        return new UpdateCheckResult(
            mgrRemote, mgrLocal, mgrDownloadUrl,
            coreRemote, coreLocal,
            mgrUpdate, coreUpdate,
            DateTime.UtcNow);
    }

    // ── Auto-update logic ──

    private static async Task RunAutoUpdateAsync(AppConfig cfg, string rootDir,
        UpdateCheckResult result)
    {
        try
        {
            // Auto-update core first (doesn't require app restart)
            if (result.CoreUpdateAvailable)
            {
                Logger.Info("Автообновление zapret core...");
                UI.ToastNotifier.Show("Zapret", "Автообновление zapret core...");
                var ok = await GitHubUpdater.UpdateZapretCoreFilesAsync(cfg, rootDir);
                if (ok)
                {
                    // Restart zapret service
                    Logger.Info("Перезапуск службы zapret после обновления core...");
                    Service.WinServiceManager.Stop("zapret");
                    await Task.Delay(2000);
                    Service.WinServiceManager.Start("zapret");
                    await Task.Delay(2000);
                    var state = Service.WinServiceManager.GetState("zapret");
                    if (state == Service.WinServiceManager.ServiceState.Running)
                        UI.ToastNotifier.Show("Zapret", "Core обновлён и служба перезапущена");
                    else
                        UI.ToastNotifier.Show("Zapret", "Core обновлён, но служба не запустилась. Проверьте через п.3");
                }
            }

            // Auto-update manager (requires app restart — do last)
            if (result.ManagerUpdateAvailable && result.ManagerDownloadUrl != null)
            {
                Logger.Info("Автообновление manager...");
                UI.ToastNotifier.Show("Zapret", "Автообновление менеджера, приложение будет перезапущено...");
                await Task.Delay(2000);
                var updated = await GitHubUpdater.UpdateManagerAsync(
                    result.ManagerDownloadUrl, rootDir, result.ManagerRemote!);
                if (updated)
                    Environment.Exit(0);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Автообновление не удалось: {ex}");
            UI.ToastNotifier.Show("Zapret — ошибка", $"Автообновление не удалось: {ex.Message}");
        }
    }

    // ── Helpers ──

    public static string GetUpdateMode(string rootDir)
    {
        var path = Path.Combine(rootDir, "utils", UpdateModeFileName);
        if (!File.Exists(path)) return "manual";
        return File.ReadAllText(path).Trim().ToLower() switch
        {
            "auto" => "auto",
            _ => "manual"
        };
    }

    public static void SetUpdateMode(string rootDir, string mode)
    {
        var utilsDir = Path.Combine(rootDir, "utils");
        Directory.CreateDirectory(utilsDir);
        File.WriteAllText(Path.Combine(utilsDir, UpdateModeFileName),
            mode == "auto" ? "auto" : "manual");
    }

    private static string? ReadLocalCoreVersion(string rootDir)
    {
        var vf = Path.Combine(rootDir, "bin", "version.txt");
        return File.Exists(vf) ? File.ReadAllText(vf).Trim() : null;
    }

    /// <summary>True if remote version is strictly newer than local (semantic comparison).</summary>
    public static bool IsNewerVersion(string? remote, string? local)
    {
        if (string.IsNullOrWhiteSpace(remote) || string.IsNullOrWhiteSpace(local))
            return false;

        // Strip common prefixes like "v", "V"
        remote = remote.TrimStart('v', 'V');
        local = local.TrimStart('v', 'V');

        // Try System.Version first (handles 2.3.0 vs 2.4.0)
        if (Version.TryParse(remote, out var remoteVer) && Version.TryParse(local, out var localVer))
            return remoteVer > localVer;

        // Fallback: segment-by-segment comparison for non-standard versions like "1.9.8c"
        var remoteParts = remote.Split('.', '-', '_');
        var localParts = local.Split('.', '-', '_');
        var maxLen = Math.Max(remoteParts.Length, localParts.Length);

        for (int i = 0; i < maxLen; i++)
        {
            var rp = i < remoteParts.Length ? remoteParts[i] : "0";
            var lp = i < localParts.Length ? localParts[i] : "0";

            // Try numeric comparison first
            if (int.TryParse(rp, out var rn) && int.TryParse(lp, out var ln))
            {
                if (rn > ln) return true;
                if (rn < ln) return false;
                continue;
            }

            // String comparison for segments like "8c" vs "8b"
            var cmp = string.Compare(rp, lp, StringComparison.OrdinalIgnoreCase);
            if (cmp > 0) return true;
            if (cmp < 0) return false;
        }

        return false; // Equal
    }

    private static void SaveCache(string rootDir, UpdateCheckResult result)
    {
        try
        {
            var utilsDir = Path.Combine(rootDir, "utils");
            Directory.CreateDirectory(utilsDir);
            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            File.WriteAllText(Path.Combine(utilsDir, CacheFileName), json);
        }
        catch (Exception ex) { Logger.Warn($"Не удалось сохранить кеш обновлений: {ex.Message}"); }
    }

    public static UpdateCheckResult? LoadCache(string rootDir)
    {
        try
        {
            var path = Path.Combine(rootDir, "utils", CacheFileName);
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UpdateCheckResult>(json,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }
        catch { return null; }
    }


}

/// <summary>Result of an update check.</summary>
public record UpdateCheckResult(
    [property: JsonPropertyName("managerRemote")] string? ManagerRemote,
    [property: JsonPropertyName("managerLocal")] string? ManagerLocal,
    [property: JsonPropertyName("managerDownloadUrl")] string? ManagerDownloadUrl,
    [property: JsonPropertyName("coreRemote")] string? CoreRemote,
    [property: JsonPropertyName("coreLocal")] string? CoreLocal,
    [property: JsonPropertyName("managerUpdateAvailable")] bool ManagerUpdateAvailable,
    [property: JsonPropertyName("coreUpdateAvailable")] bool CoreUpdateAvailable,
    [property: JsonPropertyName("checkedAt")] DateTime CheckedAt
);
