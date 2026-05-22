using ZapretManager.Core;
using ZapretManager.UI;
using ZapretManager.Service;
using ZapretManager.Diagnostics;
using Spectre.Console;

namespace ZapretManager.Menus;

/// <summary>
/// Advanced features menu actions (пункты 17–23).
/// Extracted from Program.cs as part of the ongoing refactor.
/// Note: Watchdog (п.17) and SpeedTest (п.18) require the _watchdog instance
/// and StopZapretForTest() helper — they are provided via delegates.
/// </summary>
internal static class AdvancedMenu
{
    // ── Watchdog (п.17) ──────────────────────────────────────────────────────

    internal static void Watchdog(
        ref Service.Watchdog? watchdog,
        string rootDir,
        AppConfig cfg)
    {
        Console.Clear();
        ConsoleMenu.WriteHeader("WATCHDOG (АВТОСБРОС)");
        Console.WriteLine();

        var enabled = watchdog?.IsEnabled == true;
        AnsiConsole.MarkupLine(enabled
            ? "  [green]Статус: включён[/]"
            : "  [dim]Статус: выключен[/]");
        if (watchdog != null)
        {
            var lastCheck = watchdog.LastCheck == DateTime.MinValue
                ? "не было"
                : watchdog.LastCheck.ToString("HH:mm:ss");
            ConsoleMenu.WriteInfo($"Последняя проверка: {lastCheck}");
            ConsoleMenu.WriteInfo($"Результат: {watchdog.LastStatus}");
        }
        ConsoleMenu.WriteInfo($"Интервал: {cfg.Watchdog.CheckIntervalMinutes} мин");
        ConsoleMenu.WriteInfo($"Порог сбоев: {cfg.Watchdog.FailThreshold} ошибки");
        ConsoleMenu.WriteInfo($"Остывание: {cfg.Watchdog.CooldownMinutes} мин");
        Console.WriteLine();

        // Show ranking if available
        var ranking = StrategyRanking.Load(rootDir);
        if (ranking.Count > 0)
        {
            ConsoleMenu.WriteInfo("Рейтинг стратегий:");
            var table = new Table().Border(TableBorder.Simple).HideHeaders()
                .AddColumn("").AddColumn("").AddColumn("");
            for (int i = 0; i < Math.Min(ranking.Count, 5); i++)
                table.AddRow(
                    $"[dim]{i + 1}.[/]",
                    $"[cyan]{Markup.Escape(ranking[i].Name)}[/]",
                    $"[dim]score: {ranking[i].Score}[/]");
            AnsiConsole.Write(table);
            Console.WriteLine();
        }
        else
        {
            ConsoleMenu.WriteWarn("Нет рейтинга стратегий. Запустите тест стратегий (п.11) для авторотации.");
            Console.WriteLine();
        }

        if (ConsoleMenu.Confirm($"Watchdog: {(enabled ? "выключить" : "включить")}?"))
        {
            watchdog ??= new Service.Watchdog(rootDir, cfg);
            watchdog.Toggle();
            ConsoleMenu.WriteOk($"Watchdog {(watchdog.IsEnabled ? "включён" : "выключен")}");
        }

        ConsoleMenu.PauseAny();
    }

    // ── Speed Test (п.18) ────────────────────────────────────────────────────

    internal static async Task SpeedTestAsync(Action stopZapretForTest)
    {
        Console.Clear();
        ConsoleMenu.WriteHeader("SPEED-ТЕСТ");
        Console.WriteLine();
        AnsiConsole.MarkupLine("  Измеряет скорость [bold]без обхода[/] и [bold]с обходом[/] DPI.");
        AnsiConsole.MarkupLine("  Использует Cloudflare CDN (10 MB download, 1 MB upload).");
        Console.WriteLine();

        if (!ConsoleMenu.Confirm("Начать тест?")) return;

        // Test WITHOUT bypass
        ConsoleMenu.WriteStep("БЕЗ обхода DPI");
        stopZapretForTest();
        await Task.Delay(2000);

        var before = await SpeedTester.RunAsync(msg => ConsoleMenu.WriteInfo(msg));
        SpeedTester.PrintResult(before, "без обхода");

        // Test WITH bypass
        ConsoleMenu.WriteStep("С обходом DPI");
        WinServiceManager.Start("zapret");
        await Task.Delay(3000);

        var after = await SpeedTester.RunAsync(msg => ConsoleMenu.WriteInfo(msg));
        SpeedTester.PrintResult(after, "с обходом");

        // Comparison
        SpeedTester.PrintComparison(before, after);
        Console.WriteLine();

        ConsoleMenu.PauseAny();
    }

    // ── Strategy Editor (п.19) ───────────────────────────────────────────────

    internal static void StrategyEditor(string rootDir, string binDir, string listsDir)
    {
        var strategiesDir = Path.Combine(rootDir, "strategies");
        Directory.CreateDirectory(strategiesDir);
        Service.StrategyEditor.Run(strategiesDir, binDir, listsDir);
    }

    // ── ISP Detect (п.20) ────────────────────────────────────────────────────

    internal static async Task IspDetectAsync(string rootDir)
    {
        Console.Clear();
        ConsoleMenu.WriteHeader("ОПРЕДЕЛЕНИЕ ПРОВАЙДЕРА");
        Console.WriteLine();

        // Try cache first
        var info = IspDetector.LoadCache(rootDir);
        if (info == null)
        {
            ConsoleMenu.WriteInfo("Определяю провайдера...");
            info = await IspDetector.DetectAsync();
        }

        if (info == null)
        {
            ConsoleMenu.WriteError("Не удалось определить провайдера. Проверьте соединение.");
            ConsoleMenu.PauseAny();
            return;
        }

        IspDetector.SaveCache(rootDir, info);
        IspDetector.Print(info);

        // Show recommendations
        var recs = await IspDetector.GetRecommendationsAsync(rootDir, info.Isp);
        if (recs.Count > 0)
        {
            Console.WriteLine();
            AnsiConsole.MarkupLine("  [bold cyan]Рекомендуемые стратегии:[/]");
            var table = new Table().Border(TableBorder.Simple).HideHeaders()
                .AddColumn("").AddColumn("");
            for (int i = 0; i < recs.Count; i++)
                table.AddRow($"[dim]{i + 1}.[/]", Markup.Escape(recs[i]));
            AnsiConsole.Write(table);
        }
        else
        {
            Console.WriteLine();
            ConsoleMenu.WriteInfo("Нет рекомендаций для данного ISP.");
            ConsoleMenu.WriteInfo("Запустите тест стратегий (п.11) для подбора стратегии.");
        }

        Console.WriteLine();
        ConsoleMenu.PauseAny();
    }

    // ── Domain Management (п.21) ─────────────────────────────────────────────

    internal static void Domains(string listsDir)
        => DomainManager.Run(listsDir);

    // ── NIC Selector (п.22) ──────────────────────────────────────────────────

    internal static void NicSelector(string rootDir)
        => Service.NicSelector.Run(rootDir);

    // ── Settings Export/Import (п.23) ────────────────────────────────────────

    internal static void SettingsExport(string rootDir, string listsDir)
        => SettingsExporter.Run(rootDir, listsDir);
}
