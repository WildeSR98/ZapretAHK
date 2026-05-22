using ZapretManager.Core;
using ZapretManager.Service;
using ZapretManager.UI;
using ZapretManager.Lists;
using ZapretManager.Diagnostics;
using ZapretManager.Updates;
using Spectre.Console;

namespace ZapretManager.Menus;

/// <summary>
/// Settings/updates-related menu actions (пункт 6 — UpdateMode, ScheduledTask).
/// Extracted gradually from Program.cs.
/// </summary>
internal static class SettingsMenu
{
    // ── Toggle updates (п.6) ─────────────────────────────────────────────────

    internal static void ToggleUpdates(string rootDir, string utilsDir)
    {
        Console.Clear();
        ConsoleMenu.WriteHeader("НАСТРОЙКА ОБНОВЛЕНИЙ");

        var flag    = Path.Combine(utilsDir, "check_updates.enabled");
        var enabled = File.Exists(flag);
        var mode    = UpdateChecker.GetUpdateMode(rootDir);
        var taskExists = TaskSchedulerHelper.TaskExists();

        ConsoleMenu.WriteStatusTable(new[]
        {
            ("Автопроверка",      enabled    ? "[green]включена[/]"      : "[dim]выключена[/]"),
            ("Режим",             mode == "auto" ? "[cyan]автоматический[/]" : "[dim]ручной[/]"),
            ("Задача планировщика", taskExists ? "[green]создана[/]"     : "[dim]не создана[/]"),
        });
        AnsiConsole.WriteLine();

        // Interactive selection
        var action = ConsoleMenu.SelectionPrompt(
            "Действие",
            new[]
            {
                "1. Включить / выключить проверку обновлений",
                "2. Переключить режим (ручной / авто)",
                "3. Создать задачу в планировщике Windows (автопроверка при входе)",
                "4. Удалить задачу из планировщика",
                "0. Назад",
            });

        switch (action[0])
        {
            case '1':
                if (enabled) { File.Delete(flag);                        ConsoleMenu.WriteOk("Автопроверка отключена"); }
                else          { File.WriteAllText(flag, "ВКЛЮЧЕНО");     ConsoleMenu.WriteOk("Автопроверка включена"); }
                break;

            case '2':
                var newMode = mode == "auto" ? "manual" : "auto";
                UpdateChecker.SetUpdateMode(rootDir, newMode);
                ConsoleMenu.WriteOk($"Режим обновлений: {(newMode == "auto" ? "автоматический" : "ручной")}");
                break;

            case '3':
                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrEmpty(exePath))
                {
                    ConsoleMenu.WriteError("Не удалось определить путь к exe");
                    break;
                }
                ConsoleMenu.StartSpinner("Создаю задачу в планировщике...");
                var created = TaskSchedulerHelper.CreateUpdateTask(exePath);
                ConsoleMenu.StopSpinner(created,
                    created ? "Задача ZapretManagerUpdateCheck создана (каждый час + при входе)"
                            : "Не удалось создать задачу. Запустите от администратора.");
                break;

            case '4':
                var removed = TaskSchedulerHelper.RemoveUpdateTask();
                if (removed) ConsoleMenu.WriteOk("Задача удалена из планировщика");
                else         ConsoleMenu.WriteWarn("Задача не найдена или не удалось удалить");
                break;
        }

        ConsoleMenu.PauseAny();
    }

    // ── Game filter (п.4) ────────────────────────────────────────────────────

    internal static void ConfigureGameFilter(string utilsDir)
    {
        Console.Clear();
        ConsoleMenu.WriteHeader("ИГРОВОЙ ФИЛЬТР");
        Console.WriteLine("   0. Отключить");
        Console.WriteLine("   1. TCP + UDP");
        Console.WriteLine("   2. Только TCP");
        Console.WriteLine("   3. Только UDP");
        var ch   = ConsoleMenu.Prompt("Выберите режим (0-3)", "0");
        var mode = ch switch { "1" => "all", "2" => "tcp", "3" => "udp", _ => "disabled" };
        Service.GameFilter.Set(utilsDir, mode);
        ConsoleMenu.WriteOk($"Игровой фильтр: {Service.GameFilter.StatusLabel(utilsDir)}");
        ConsoleMenu.WriteWarn("Перезапустите zapret для применения изменений");
        ConsoleMenu.PauseAny();
    }

    // ── IPSet switch (п.5) ───────────────────────────────────────────────────

    internal static void IpsetSwitch(string listsDir)
    {
        Console.Clear();
        ConsoleMenu.WriteHeader("IPSET ФИЛЬТР");
        var listFile   = Path.Combine(listsDir, "ipset-all.txt");
        var backupFile = listFile + ".backup";
        var status     = IpsetTestHelper.GetStatus(listsDir);
        ConsoleMenu.WriteInfo($"Текущий режим: {status}");
        Console.WriteLine("   Цикл переключения: loaded → none → any → loaded");
        AnsiConsole.WriteLine();

        switch (status)
        {
            case "loaded":
                if (File.Exists(backupFile)) File.Delete(backupFile);
                File.Move(listFile, backupFile);
                File.WriteAllText(listFile, "203.0.113.113/32\r\n");
                ConsoleMenu.WriteOk("Переключено в режим 'none'");
                break;
            case "none":
                File.WriteAllText(listFile, "\r\n");
                ConsoleMenu.WriteOk("Переключено в режим 'any'");
                break;
            case "any":
                if (File.Exists(backupFile))
                {
                    File.Delete(listFile);
                    File.Move(backupFile, listFile);
                    ConsoleMenu.WriteOk("Переключено в режим 'loaded'");
                }
                else ConsoleMenu.WriteError("Нет резервной копии. Обновите список IPSet через п.7.");
                break;
        }
        ConsoleMenu.PauseAny();
    }
}
