using System.Net.Http;
using ZapretManager.Core;
using ZapretManager.UI;

namespace ZapretManager.Service;

/// <summary>
/// Background watchdog: monitors target accessibility and auto-rotates strategies on failure.
/// </summary>
public sealed class Watchdog : IDisposable
{
    private readonly string _rootDir;
    private readonly AppConfig _cfg;
    private System.Threading.Timer? _timer;
    private int _consecutiveFailures;
    private DateTime _lastRotation = DateTime.MinValue;
    private bool _disposed;
    private bool _enabled;

    public bool IsEnabled => _enabled;
    public DateTime LastCheck { get; private set; }
    public string LastStatus { get; private set; } = "не проверялось";

    public Watchdog(string rootDir, AppConfig cfg)
    {
        _rootDir = rootDir;
        _cfg = cfg;
    }

    public void Start()
    {
        if (_enabled) return;
        _enabled = true;
        var interval = Math.Max(_cfg.Watchdog.CheckIntervalMinutes, 1) * 60 * 1000;
        _timer = new System.Threading.Timer(_ => CheckTargetsAsync().Wait(), null, 30_000, interval);
        Logger.Info($"Watchdog запущен (интервал: {_cfg.Watchdog.CheckIntervalMinutes} мин)");
    }

    public void Stop()
    {
        _enabled = false;
        _timer?.Dispose();
        _timer = null;
        Logger.Info("Watchdog остановлен");
    }

    public void Toggle()
    {
        if (_enabled) Stop();
        else Start();

        // Save flag
        var flag = Path.Combine(_rootDir, "utils", "watchdog.enabled");
        if (_enabled) File.WriteAllText(flag, "1");
        else if (File.Exists(flag)) File.Delete(flag);
    }

    public static bool IsEnabledFlag(string rootDir)
        => File.Exists(Path.Combine(rootDir, "utils", "watchdog.enabled"));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    private async Task CheckTargetsAsync()
    {
        if (!_enabled) return;

        try
        {
            var targets = _cfg.Diagnostics.CheckTargets;
            if (targets.Count == 0) return;

            int ok = 0, fail = 0;
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            foreach (var target in targets)
            {
                try
                {
                    if (string.IsNullOrEmpty(target.Url)) { ok++; continue; }
                    var response = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, target.Url));
                    if (response.IsSuccessStatusCode || (int)response.StatusCode < 500)
                        ok++;
                    else
                        fail++;
                }
                catch
                {
                    fail++;
                }
            }

            LastCheck = DateTime.Now;
            var total = ok + fail;
            var failPercent = total > 0 ? (fail * 100 / total) : 0;

            if (failPercent >= _cfg.Watchdog.FailPercent)
            {
                _consecutiveFailures++;
                LastStatus = $"СБОЙ ({fail}/{total} недоступны), попытка {_consecutiveFailures}/{_cfg.Watchdog.FailThreshold}";
                Logger.Warn($"Watchdog: {LastStatus}");

                if (_consecutiveFailures >= _cfg.Watchdog.FailThreshold)
                {
                    await TryRotateStrategyAsync();
                    _consecutiveFailures = 0;
                }
            }
            else
            {
                if (_consecutiveFailures > 0)
                    Logger.Info($"Watchdog: сайты снова доступны ({ok}/{total})");
                _consecutiveFailures = 0;
                LastStatus = $"OK ({ok}/{total} доступны)";
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Watchdog check error: {ex.Message}");
            LastStatus = $"Ошибка: {ex.Message}";
        }
    }

    private async Task TryRotateStrategyAsync()
    {
        // Cooldown check
        var elapsed = DateTime.Now - _lastRotation;
        if (elapsed.TotalMinutes < _cfg.Watchdog.CooldownMinutes)
        {
            Logger.Info($"Watchdog: кулдаун ({_cfg.Watchdog.CooldownMinutes - (int)elapsed.TotalMinutes} мин)");
            return;
        }

        // Load ranking
        var ranking = StrategyRanking.Load(_rootDir);
        if (ranking.Count == 0)
        {
            Logger.Warn("Watchdog: нет результатов тестов для авторотации. Запустите тест стратегий.");
            ToastNotifier.Show("Zapret Watchdog", "Обнаружена проблема, но нет данных для авторотации. Запустите тест стратегий.");
            return;
        }

        // Get current strategy
        string? currentStrategy = null;
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"System\CurrentControlSet\Services\zapret");
            currentStrategy = key?.GetValue("zapret-discord-youtube")?.ToString();
        }
        catch (Exception ex) { Logger.Error($"[Watchdog] {ex.GetType().Name}: {ex.Message}"); }

        var maxAttempts = Math.Min(_cfg.Watchdog.MaxRotations, ranking.Count);

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            var next = StrategyRanking.GetNext(ranking, currentStrategy);
            if (next == null) break;

            Logger.Info($"Watchdog: попытка {attempt + 1}/{maxAttempts} — переключение на {next.Name}");
            ToastNotifier.Show("Zapret Watchdog",
                $"Обнаружена проблема. Переключение на {next.Name}...");

            try
            {
                // Stop current
                var state = WinServiceManager.GetState("zapret");
                if (state == WinServiceManager.ServiceState.Running)
                    WinServiceManager.Stop("zapret");
                ProcessManager.KillAll();

                // Install new strategy
                var winwsExe = Path.Combine(_rootDir, "bin", "winws.exe");
                var listsDir = Path.Combine(_rootDir, "lists");
                var utilsDir = Path.Combine(_rootDir, "utils");
                var gf = GameFilter.Get(utilsDir);
                var batArgs = StrategyReader.ParseArgs(next.FullPath,
                    Path.Combine(_rootDir, "bin"), listsDir, gf.Tcp, gf.Udp);
                var binPath = $"\"{winwsExe}\" {batArgs}";

                WinServiceManager.Remove("zapret");
                WinServiceManager.Install("zapret", "zapret", "Zapret DPI bypass", binPath);

                // Save strategy name
                using (var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(
                    @"System\CurrentControlSet\Services\zapret"))
                {
                    key?.SetValue("zapret-discord-youtube",
                        Path.GetFileNameWithoutExtension(next.Name));
                }

                WinServiceManager.Start("zapret");
                _lastRotation = DateTime.Now;

                // Wait and verify
                await Task.Delay(10_000);

                // Quick check
                int quickOk = 0;
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                foreach (var t in _cfg.Diagnostics.CheckTargets.Take(3))
                {
                    try
                    {
                        if (string.IsNullOrEmpty(t.Url)) continue;
                        var r = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, t.Url));
                        if (r.IsSuccessStatusCode) quickOk++;
                    }
                    catch (Exception ex) { Logger.Error($"[Watchdog] {ex.GetType().Name}: {ex.Message}"); }
                }

                if (quickOk > 0)
                {
                    Logger.Ok($"Watchdog: переключено на {next.Name} (проверка OK)");
                    ToastNotifier.Show("Zapret Watchdog",
                        $"Переключено на {Path.GetFileNameWithoutExtension(next.Name)}");
                    LastStatus = $"Авторотация → {next.Name}";
                    return;
                }

                currentStrategy = Path.GetFileNameWithoutExtension(next.Name);
                Logger.Warn($"Watchdog: {next.Name} тоже не работает, пробую следующую...");
            }
            catch (Exception ex)
            {
                Logger.Error($"Watchdog rotation failed: {ex.Message}");
            }
        }

        Logger.Error("Watchdog: все стратегии испробованы, ни одна не помогла");
        ToastNotifier.Show("Zapret Watchdog",
            "Все стратегии испробованы. Проверьте подключение к интернету.");
        LastStatus = "Авторотация не помогла";
    }
}
