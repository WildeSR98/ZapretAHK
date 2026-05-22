using ZapretManager.Core;
using ZapretManager.UI;
using ZapretManager.Lists;
using ZapretManager.Updates;
using ZapretManager.Service;

namespace ZapretManager.Menus;

/// <summary>
/// Update-related menu actions: UpdateIpset, UpdateHosts, CheckUpdates (пункты 7-9).
/// Extracted from Program.cs as part of the ongoing refactor.
/// </summary>
internal static class UpdateMenu
{
    // ── Update IPSet (п.7) ───────────────────────────────────────────────────

    internal static async Task UpdateIpsetAsync(string listsDir, string ipsetServiceUrl)
    {
        Console.Clear();
        ConsoleMenu.WriteHeader("ОБНОВЛЕНИЕ IPSET");
        var listFile = Path.Combine(listsDir, "ipset-all.txt");
        ConsoleMenu.StartSpinner("Скачиваю ipset-all.txt...");
        try
        {
            using var http = new System.Net.Http.HttpClient();
            var content  = await http.GetStringAsync(ipsetServiceUrl);
            var newLines = content.Split('\n').Select(l => l.TrimEnd('\r'));
            var merged   = ListMerger.Merge(listFile, newLines);
            ListMerger.WriteUtf8(listFile, merged);
            ConsoleMenu.StopSpinner(true, $"ipset-all.txt обновлён ({merged.Length} строк)");
        }
        catch (Exception ex)
        {
            ConsoleMenu.StopSpinner(false, $"Ошибка: {ex.Message}");
        }
        ConsoleMenu.PauseAny();
    }

    // ── Update Hosts (п.8) ───────────────────────────────────────────────────

    internal static async Task UpdateHostsAsync(string hostsServiceUrl)
    {
        Console.Clear();
        ConsoleMenu.WriteHeader("ОБНОВЛЕНИЕ HOSTS");
        ConsoleMenu.StartSpinner("Проверяю файл hosts...");
        try
        {
            var needsUpdate = await HostsUpdater.CheckAndUpdate(hostsServiceUrl);
            ConsoleMenu.StopSpinner(!needsUpdate, needsUpdate
                ? "Запись обновлена или добавлена в файл hosts"
                : "В hosts всё актуально");
        }
        catch (Exception ex)
        {
            ConsoleMenu.StopSpinner(false, $"Ошибка: {ex.Message}");
        }
        ConsoleMenu.PauseAny();
    }

    // ── Check updates (п.9) ──────────────────────────────────────────────────

    internal static async Task CheckUpdatesAsync(AppConfig cfg, string rootDir, string binDir)
    {
        Console.Clear();
        ConsoleMenu.WriteHeader("ПРОВЕРКА ОБНОВЛЕНИЙ");

        var localCoreVer = ReadLocalCoreVersion(binDir);
        ConsoleMenu.WriteInfo($"Текущие версии: Manager v{GitHubUpdater.ReadManagerVersion(rootDir) ?? "не определена"} | Zapret Core {localCoreVer}");
        ConsoleMenu.WriteInfo($"Режим: {(UpdateChecker.GetUpdateMode(rootDir) == "auto" ? "автоматический" : "ручной")}");
        Console.WriteLine();

        ConsoleMenu.StartSpinner("Запрос к GitHub...");
        var result = await UpdateChecker.CheckNowAsync(cfg, rootDir);
        ConsoleMenu.StopSpinner();

        // Manager
        ConsoleMenu.WriteStep("Zapret Manager (WildeSR98/12345)");
        if (result.ManagerRemote == null)
            ConsoleMenu.WriteInfo("Не удалось проверить версию (нет доступа к GitHub)");
        else if (!result.ManagerUpdateAvailable)
            ConsoleMenu.WriteOk($"Актуально: v{result.ManagerLocal}");
        else
        {
            ConsoleMenu.WriteWarn($"Доступно v{result.ManagerRemote} (у вас: v{result.ManagerLocal ?? "?"}).");
            if (result.ManagerDownloadUrl != null && ConsoleMenu.Confirm("Обновить менеджер?"))
            {
                var ok = await GitHubUpdater.UpdateManagerAsync(result.ManagerDownloadUrl, rootDir, result.ManagerRemote);
                if (ok) { ConsoleMenu.WriteOk("Перезапуск..."); Environment.Exit(0); return; }
            }
        }

        // Core
        ConsoleMenu.WriteStep("Zapret Core (Flowseal/zapret-discord-youtube)");
        if (result.CoreRemote == null)
            ConsoleMenu.WriteInfo("Не удалось проверить версию (нет сети)");
        else if (!result.CoreUpdateAvailable)
            ConsoleMenu.WriteOk($"Актуально: {result.CoreLocal}");
        else
        {
            ConsoleMenu.WriteWarn($"Доступно {result.CoreRemote} (у вас: {result.CoreLocal ?? "?"}).");
            if (ConsoleMenu.Confirm("Обновить zapret core (bin, strategies, lists)?"))
            {
                await GitHubUpdater.UpdateZapretCoreFilesAsync(cfg, rootDir);
                // Перезапуск службы
                ConsoleMenu.WriteStep("Перезапуск службы zapret...");
                WinServiceManager.Stop("zapret");
                await Task.Delay(2000);
                WinServiceManager.Start("zapret");
                await Task.Delay(2000);
                var state = WinServiceManager.GetState("zapret");
                if (state == WinServiceManager.ServiceState.Running)
                    ConsoleMenu.WriteOk("Служба zapret запущена");
                else
                    ConsoleMenu.WriteWarn("Служба не запустилась. Переустановите через п.1");
            }
        }

        ConsoleMenu.PauseAny();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string ReadLocalCoreVersion(string binDir)
    {
        var vf = Path.Combine(binDir, "version.txt");
        if (File.Exists(vf))
        {
            var ver = File.ReadAllText(vf).Trim();
            if (!string.IsNullOrEmpty(ver)) return ver;
        }
        return "не определена";
    }
}
