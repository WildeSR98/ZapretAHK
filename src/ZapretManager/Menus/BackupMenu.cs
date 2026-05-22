using ZapretManager.Core;
using ZapretManager.UI;
using ZapretManager.Service;
using Spectre.Console;

namespace ZapretManager.Menus;

/// <summary>
/// Backup/restore and profiles menu actions (пункты 14-15).
/// Extracted from Program.cs as part of the ongoing refactor.
/// </summary>
internal static class BackupMenu
{
    // ── Backup / Restore (п.14) ──────────────────────────────────────────────

    internal static void Backup(string rootDir, int keepCount)
    {
        Console.Clear();
        ConsoleMenu.WriteHeader("БЭКАП / ВОССТАНОВЛЕНИЕ");

        var backups = BackupManager.ListBackups(rootDir);
        Console.WriteLine($"\n   Доступных бэкапов: {backups.Length}");
        for (int i = 0; i < backups.Length; i++)
        {
            var b = backups[i];
            Console.WriteLine($"     [{i + 1}] {b.Name}  ({b.Length / 1024} KB, {b.CreationTime:dd.MM.yyyy HH:mm})");
        }

        Console.WriteLine("\n   [C] Создать новый бэкап");
        if (backups.Length > 0) Console.WriteLine("   [R] Восстановить из бэкапа");
        Console.WriteLine("   [0] Назад");

        var ch = ConsoleMenu.Prompt("\n   Выбор", "0");
        switch (ch?.ToUpper())
        {
            case "C":
                BackupManager.CreateBackup(rootDir, keepCount);
                ConsoleMenu.PauseAny();
                break;
            case "R":
                if (backups.Length == 0) break;
                var pickStr = ConsoleMenu.Prompt("   Номер бэкапа");
                if (int.TryParse(pickStr, out var pick) && pick >= 1 && pick <= backups.Length)
                {
                    if (ConsoleMenu.Confirm($"Восстановить из {backups[pick - 1].Name}? Все данные будут перезаписаны."))
                    {
                        BackupManager.RestoreBackup(rootDir, backups[pick - 1].FullName);
                        ConsoleMenu.WriteInfo("Перезапустите zapret для применения изменений.");
                    }
                }
                ConsoleMenu.PauseAny();
                break;
        }
    }

    // ── Profiles (п.15) ──────────────────────────────────────────────────────

    internal static void Profiles(string rootDir, string binDir, string listsDir, string utilsDir)
    {
        Console.Clear();
        ConsoleMenu.WriteHeader("ПРОФИЛИ");

        var profiles = ProfileManager.ListProfiles(rootDir);
        AnsiConsole.MarkupLine($"\n   [dim]Доступных профилей:[/] [cyan]{profiles.Length}[/]");
        for (int i = 0; i < profiles.Length; i++)
        {
            var p = profiles[i];
            AnsiConsole.MarkupLine(
                $"     [cyan][[{i + 1}]] {Markup.Escape(p.Name)}[/]  " +
                $"[dim](стратегия: {Markup.Escape(p.Strategy)}, ipset: {Markup.Escape(p.IpsetMode)}, обновления: {Markup.Escape(p.UpdateMode)})[/]");
        }

        Console.WriteLine("\n   [S] Сохранить текущий профиль");
        if (profiles.Length > 0)
        {
            Console.WriteLine("   [A] Применить профиль");
            Console.WriteLine("   [D] Удалить профиль");
        }
        Console.WriteLine("   [0] Назад");

        var ch = ConsoleMenu.Prompt("\n   Выбор", "0");
        switch (ch?.ToUpper())
        {
            case "S":
                var name = ConsoleMenu.Prompt("   Имя профиля");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    ProfileManager.SaveProfile(rootDir, name);
                    ConsoleMenu.WriteOk($"Профиль '{name}' сохранён");
                }
                ConsoleMenu.PauseAny();
                break;
            case "A":
                if (profiles.Length == 0) break;
                var applyStr = ConsoleMenu.Prompt("   Номер профиля");
                if (int.TryParse(applyStr, out var applyIdx) && applyIdx >= 1 && applyIdx <= profiles.Length)
                {
                    var prof = profiles[applyIdx - 1];
                    if (ConsoleMenu.Confirm($"Применить профиль '{prof.Name}'?"))
                    {
                        ConsoleMenu.StartSpinner("Применяю профиль...");
                        ProfileManager.ApplyProfile(prof, rootDir, binDir, listsDir, utilsDir);
                        ConsoleMenu.StopSpinner(true, $"Профиль '{prof.Name}' применён");
                    }
                }
                ConsoleMenu.PauseAny();
                break;
            case "D":
                if (profiles.Length == 0) break;
                var delStr = ConsoleMenu.Prompt("   Номер профиля для удаления");
                if (int.TryParse(delStr, out var delIdx) && delIdx >= 1 && delIdx <= profiles.Length)
                {
                    ProfileManager.DeleteProfile(rootDir, profiles[delIdx - 1].Name);
                    ConsoleMenu.WriteOk("Профиль удалён");
                }
                ConsoleMenu.PauseAny();
                break;
        }
    }
}
