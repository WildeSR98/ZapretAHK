using ZapretManager.Core;
using ZapretManager.UI;
using ZapretManager.Diagnostics;

namespace ZapretManager.Menus;

/// <summary>
/// Diagnostics and reporting menu actions (пункты 10, 12).
/// Extracted from Program.cs as part of the ongoing refactor.
/// Note: RunTests (п.11) stays in Program.cs due to tight coupling with
/// StopZapretForTest/RestoreZapretAfterTestAsync helpers.
/// </summary>
internal static class DiagnosticsMenu
{
    // ── Diagnostics (п.10) ───────────────────────────────────────────────────

    internal static async Task DiagnosticsAsync(string binDir, string rootDir, AppConfig cfg)
    {
        Console.Clear();
        ConsoleMenu.WriteHeader("ДИАГНОСТИКА");

        // 1. Проверка файлов (как будто запускаем service.bat)
        ConsoleMenu.WriteStep("Проверка файлов конфигурации...");
        var diagResults = await FullDiagnostics.RunAllAsync(binDir);
        foreach (var r in diagResults)
        {
            switch (r.Level)
            {
                case DiagLevel.Ok:      ConsoleMenu.WriteOk(r.Message); break;
                case DiagLevel.Warning: ConsoleMenu.WriteWarn(r.Message); break;
                case DiagLevel.Error:   ConsoleMenu.WriteError(r.Message); break;
            }
            if (r.HelpUrl != null)
                ConsoleMenu.WriteInfo($"  → {r.HelpUrl}");
        }
        Console.WriteLine();

        // 2. Конфликтующие службы
        var conflicts = ConflictDetector.FindConflicts(cfg.Diagnostics.ConflictingServices);
        if (conflicts.Count > 0)
        {
            ConsoleMenu.WriteWarn($"Конфликтующие службы: {string.Join(", ", conflicts)}");
            if (ConsoleMenu.Confirm("Удалить конфликтующие службы?"))
                ConflictDetector.RemoveConflicts(conflicts);
        }
        else
            ConsoleMenu.WriteOk("Конфликтующих служб не найдено");
        Console.WriteLine();

        // 3. HTTP/ping тест доступности
        ConsoleMenu.WriteStep("Проверка доступности сайтов...");
        var accessResults = await AccessChecker.CheckAllAsync(cfg.Diagnostics.CheckTargets);
        foreach (var r in accessResults)
        {
            if (r.Reachable) ConsoleMenu.WriteOk($"{r.Name}: {r.Detail}");
            else             ConsoleMenu.WriteWarn($"{r.Name}: недоступен");
        }
        Console.WriteLine();

        // 4. Проверка файлов (возможно нужны исключения антивируса)
        var missing = AntivirusExcluder.CheckMissingFiles(binDir, rootDir);
        if (missing.Count > 0)
        {
            ConsoleMenu.WriteError($"Отсутствующие файлы (возможна блокировка антивирусом): {string.Join(", ", missing)}");
            if (ConsoleMenu.Confirm("Добавить в исключения Windows Defender?"))
                AntivirusExcluder.AddFolderExclusion(rootDir);
        }
        else
            ConsoleMenu.WriteOk("Все нужные файлы найдены");
        Console.WriteLine();

        // 5. Проверка DNS
        ConsoleMenu.WriteStep("Проверка DNS-резолвинга...");
        var dnsResults = await DnsChecker.CheckAllAsync(cfg.Diagnostics.CheckTargets);
        DnsChecker.PrintResults(dnsResults);

        // 6. Очистка кэша Discord
        if (ConsoleMenu.Confirm("Очистить кэш Discord?"))
        {
            var (closed, deleted, failed) = await DiscordCacheCleaner.Clean();
            if (closed) ConsoleMenu.WriteOk("Discord закрыт");
            foreach (var d in deleted) ConsoleMenu.WriteOk($"Удалено: {d}");
            foreach (var f in failed)  ConsoleMenu.WriteError($"Не удалось удалить: {f}");
            if (deleted.Count == 0 && failed.Count == 0)
                ConsoleMenu.WriteInfo("Кэш Discord не найден");
        }

        ConsoleMenu.PauseAny();
    }

    // ── Export report (п.12) ─────────────────────────────────────────────────

    internal static void ExportReport(string rootDir)
    {
        Console.Clear();
        ConsoleMenu.WriteHeader("ЭКСПОРТ ОТЧЁТА");
        var path = ReportExporter.Export(rootDir);
        ConsoleMenu.WriteOk($"Сохранён: {path}");
        if (ConsoleMenu.Confirm("Открыть файл?"))
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        ConsoleMenu.PauseAny();
    }
}
