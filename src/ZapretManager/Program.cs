using ZapretManager.Core;
using ZapretManager.UI;
using ZapretManager.Service;
using ZapretManager.Lists;
using ZapretManager.Diagnostics;
using ZapretManager.Updates;
using ZapretManager.Menus;
using Spectre.Console;

namespace ZapretManager;

class Program
{
    static string RootDir = "";
    static string BinDir  = "";
    static string ListsDir = "";
    static string UtilsDir = "";
    static AppConfig Cfg = new();
    static TrayManager? _tray;
    static Watchdog? _watchdog;

    static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.Title = "Zapret Auto-Setup";

        RootDir  = DetectRootDir();
        BinDir   = Path.Combine(RootDir, "bin");
        ListsDir = Path.Combine(RootDir, "lists");
        UtilsDir = Path.Combine(RootDir, "utils");
        Directory.CreateDirectory(UtilsDir);

        Cfg = AppConfig.Load(RootDir);
        Logger.Init(RootDir, Cfg.Features.VerboseLogging, Cfg.Features.LogRetentionDays);
        AdminHelper.RequireAdmin();

        // Graceful shutdown
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            UpdateChecker.Stop();
            Console.CursorVisible = true;
            Console.ResetColor();
            Logger.Info("=== Завершение по Ctrl+C ===");
            Logger.Dispose();
            Environment.Exit(0);
        };

        // Запуск фоновой проверки обновлений
        UpdateChecker.StartBackground(Cfg, RootDir);

        // Watchdog (если включён)
        if (Watchdog.IsEnabledFlag(RootDir))
        {
            _watchdog = new Watchdog(RootDir, Cfg);
            _watchdog.Start();
        }

        // Проверка здоровья службы (ImagePath может устареть если папка перемещена)
        await CheckServiceHealth();

        // Запуск tray-процесса (отдельный процесс, живёт независимо от консоли)
        if (!args.Contains("--tray") && !args.Contains("--check-updates"))
            EnsureTrayProcess();

        if (args.Contains("--check-updates"))
        {
            // Silent check for Task Scheduler — toast only, no console
            try
            {
                var result = await UpdateChecker.CheckNowAsync(Cfg, RootDir);
                if (result.ManagerUpdateAvailable || result.CoreUpdateAvailable)
                {
                    var parts = new List<string>();
                    if (result.ManagerUpdateAvailable) parts.Add($"Manager v{result.ManagerRemote}");
                    if (result.CoreUpdateAvailable) parts.Add($"Core {result.CoreRemote}");
                    ToastNotifier.Show("Zapret — доступно обновление",
                        $"Новая версия: {string.Join(" | ", parts)}");
                }
            }
            catch (Exception ex) { Logger.Error($"[Program] {ex.GetType().Name}: {ex.Message}"); }
            return;
        }

        if (args.Contains("--menu"))        { Console.Title = "Zapret Manager"; await RunMenuAsync(); return; }
        if (args.Contains("--tray"))         { await RunTrayMode(); return; }
        if (args.Contains("--remove"))      { await RunRemoveAsync(); return; }
        if (args.Contains("--reinstall"))   { await RunRemoveAsync(silent: true); await RunSetupAsync(args); return; }
        if (args.Contains("--test"))        { await RunTestAndInstallAsync(); return; }
        if (args.Contains("--diagnostics")) { await MenuDiagnostics(); MenuExportReport(); return; }

        // Default — run setup wizard (like autosetup.bat)
        await RunSetupAsync(args);
    }

    // ── MAIN MENU ─────────────────────────────────────────────────────────────
    static async Task RunMenuAsync()
    {
        while (true)
        {
            Console.Clear();
            PrintMenuHeader();

            Console.Write("   Выберите вариант (0-23): ");
            var choice = Console.ReadLine()?.Trim();

            switch (choice)
            {
                case "1":  await MenuInstallService();   break;
                case "2":  await MenuRemoveServices();   break;
                case "3":  MenuServiceStatus();           break;
                case "4":  MenuGameFilter();              break;
                case "5":  MenuIpsetSwitch();             break;
                case "6":  MenuToggleUpdates();           break;
                case "7":  await MenuUpdateIpset();       break;
                case "8":  await MenuUpdateHosts();       break;
                case "9":  await MenuCheckUpdates();      break;
                case "10": await MenuDiagnostics();       break;
                case "11": await MenuRunTests();          break;
                case "12": MenuExportReport();            break;
                case "13": await MenuTgProxy();                 break;
                case "14": MenuBackup();                   break;
                case "15": MenuProfiles();                 break;
                case "16": await MenuTrafficMonitor();     break;
                case "17": MenuWatchdog();                  break;
                case "18": await MenuSpeedTest();            break;
                case "19": MenuStrategyEditor();             break;
                case "20": await MenuIspDetect();           break;
                case "21": MenuDomains();                   break;
                case "22": MenuNicSelector();               break;
                case "23": MenuSettingsExport();            break;
                case "0":  return;
            }
        }
    }

    static void PrintMenuHeader()
    {
        var version  = Cfg.Project.Version;
        var mgrVer   = GitHubUpdater.ReadManagerVersion(RootDir) ?? version;
        var coreVer  = ReadLocalCoreVersion();
        var state    = WinServiceManager.GetState("zapret");
        var strategy = GetCurrentStrategy();
        var gf       = GameFilter.StatusLabel(UtilsDir);
        var ipset    = GetIpsetStatus();
        var updates  = File.Exists(Path.Combine(UtilsDir, "check_updates.enabled")) ? "вкл" : "выкл";
        var tgState  = IsTgProxyRunning() ? "[green]запущен[/]" : "[dim]остановлен[/]";
        var upd      = UpdateChecker.LastResult ?? UpdateChecker.LoadCache(RootDir);
        var updateMode = UpdateChecker.GetUpdateMode(RootDir) == "auto" ? "авто" : "ручной";
        var wdStatus = _watchdog?.IsEnabled == true ? "[green]вкл[/]" : "[dim]выкл[/]";

        // ── Header banner ────────────────────────────────────────────────────
        AnsiConsole.Write(new Rule($"[bold cyan]Zapret Manager[/] [dim]v{Markup.Escape(mgrVer)}[/]")
            .RuleStyle("cyan dim"));

        // ── Status table ─────────────────────────────────────────────────────
        var (stateLabel, stateColour) = state switch
        {
            WinServiceManager.ServiceState.Running      => ("запущена",      "green"),
            WinServiceManager.ServiceState.Stopped      => ("остановлена",   "yellow"),
            WinServiceManager.ServiceState.NotInstalled => ("не установлена","red"),
            _                                           => ("неизвестно",    "grey"),
        };
        var mgrColour  = (upd != null && upd.ManagerUpdateAvailable) ? "yellow" : "green";
        var coreColour = coreVer == "не установлен" ? "red"
            : (upd != null && upd.CoreUpdateAvailable) ? "yellow" : "green";

        ConsoleMenu.WriteStatusTable(new[]
        {
            ("Manager",   $"[{mgrColour}]v{Markup.Escape(mgrVer)}[/]"),
            ("Core",      $"[{coreColour}]{Markup.Escape(coreVer)}[/]"),
            ("Служба",    $"[{stateColour}]{Markup.Escape(stateLabel)}[/]"),
            ("Стратегия", $"[cyan]{Markup.Escape(strategy)}[/]"),
            ("Обновления",$"{Markup.Escape(updates)} [dim]{Markup.Escape(updateMode)}[/]"),
            ("TG Proxy",  tgState),
            ("Watchdog",  wdStatus),
        });

        // ── Update banner ─────────────────────────────────────────────────────
        if (upd != null && (upd.ManagerUpdateAvailable || upd.CoreUpdateAvailable))
        {
            var parts = new List<string>();
            if (upd.ManagerUpdateAvailable) parts.Add($"Manager v{upd.ManagerRemote}");
            if (upd.CoreUpdateAvailable)    parts.Add($"Core {upd.CoreRemote}");
            AnsiConsole.MarkupLine($"  [bold yellow]⚠ Доступно обновление: {Markup.Escape(string.Join(" | ", parts))}  →  п.9[/]");
        }

        // ── Menu items ────────────────────────────────────────────────────────
        AnsiConsole.Write(new Rule().RuleStyle("grey dim"));
        AnsiConsole.MarkupLine("  [bold]:: СЛУЖБА[/]");
        AnsiConsole.MarkupLine("      [cyan]1.[/] Установить службу");
        AnsiConsole.MarkupLine("      [cyan]2.[/] Удалить службы");
        AnsiConsole.MarkupLine("      [cyan]3.[/] Проверить статус");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [bold]:: НАСТРОЙКИ[/]");
        AnsiConsole.MarkupLine($"      [cyan]4.[/] Игровой фильтр       [dim]{Markup.Escape(gf)}[/]");
        AnsiConsole.MarkupLine($"      [cyan]5.[/] IPSet фильтр         [dim]{Markup.Escape(ipset)}[/]");
        AnsiConsole.MarkupLine($"      [cyan]6.[/] Обновления           [dim]{Markup.Escape(updates)} | {Markup.Escape(updateMode)}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [bold]:: ОБНОВЛЕНИЯ[/]");
        AnsiConsole.MarkupLine("      [cyan]7.[/] Обновить список IPSet");
        AnsiConsole.MarkupLine("      [cyan]8.[/] Обновить файл Hosts");
        AnsiConsole.MarkupLine("      [cyan]9.[/] Проверить обновления");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [bold]:: ИНСТРУМЕНТЫ[/]");
        AnsiConsole.MarkupLine("     [cyan]10.[/] Диагностика");
        AnsiConsole.MarkupLine("     [cyan]11.[/] Тест стратегий");
        AnsiConsole.MarkupLine("     [cyan]12.[/] Экспорт отчёта");
        AnsiConsole.MarkupLine($"     [cyan]13.[/] TG WS Proxy         {tgState}");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [bold]:: СЕРВИС[/]");
        AnsiConsole.MarkupLine("     [cyan]14.[/] Бэкап / Восстановление");
        AnsiConsole.MarkupLine("     [cyan]15.[/] Профили");
        AnsiConsole.MarkupLine("     [cyan]16.[/] Мониторинг трафика");
        AnsiConsole.MarkupLine($"     [cyan]17.[/] Watchdog (авторотация) {wdStatus}");
        AnsiConsole.MarkupLine("     [cyan]18.[/] Speed-тест");
        AnsiConsole.MarkupLine("     [cyan]19.[/] Редактор стратегий");
        AnsiConsole.MarkupLine("     [cyan]20.[/] Определение провайдера");
        AnsiConsole.MarkupLine("     [cyan]21.[/] Управление доменами");
        AnsiConsole.MarkupLine("     [cyan]22.[/] Сетевой адаптер");
        AnsiConsole.MarkupLine("     [cyan]23.[/] Экспорт/Импорт настроек");
        AnsiConsole.Write(new Rule().RuleStyle("grey dim"));
        AnsiConsole.MarkupLine("      [dim]0.[/] Выход");
        AnsiConsole.WriteLine();
    }

    // ── MENU ACTIONS ──────────────────────────────────────────────────────────

    // ── Delegate to ServiceMenu ──────────────────────────────────────────────
    static async Task MenuInstallService()  => await ServiceMenu.InstallAsync(RootDir, BinDir, ListsDir, UtilsDir);
    static async Task MenuRemoveServices()  => await ServiceMenu.RemoveAsync();
    static void      MenuServiceStatus()    =>       ServiceMenu.Status(BinDir, UtilsDir, RootDir);


    static void MenuGameFilter() => SettingsMenu.ConfigureGameFilter(UtilsDir);

    static void MenuIpsetSwitch() => SettingsMenu.IpsetSwitch(ListsDir);

    static void MenuToggleUpdates() => SettingsMenu.ToggleUpdates(RootDir, UtilsDir);


    static async Task MenuUpdateIpset() => await UpdateMenu.UpdateIpsetAsync(ListsDir, Cfg.Repositories.ZapretCore.IpsetService ?? "");

    static async Task MenuUpdateHosts() => await UpdateMenu.UpdateHostsAsync(Cfg.Repositories.ZapretCore.HostsService ?? "");

    static async Task MenuCheckUpdates() => await UpdateMenu.CheckUpdatesAsync(Cfg, RootDir, BinDir);

    static async Task MenuDiagnostics() => await DiagnosticsMenu.DiagnosticsAsync(BinDir, RootDir, Cfg);

    // ── BACKUP ────────────────────────────────────────────────────────────────
    static void MenuBackup() => BackupMenu.Backup(RootDir, Cfg.Backup.KeepCount);

    // ── PROFILES ──────────────────────────────────────────────────────────────
    static void MenuProfiles() => BackupMenu.Profiles(RootDir, BinDir, ListsDir, UtilsDir);

    // ── TRAFFIC MONITOR ───────────────────────────────────────────────────────
    static async Task MenuTrafficMonitor() => await MonitorMenu.TrafficMonitorAsync();

    static async Task MenuRunTests()
    {
        Console.Clear();
        ConsoleMenu.WriteHeader("ТЕСТЫ СТРАТЕГИЙ");

        // Check for interrupted test (ipset flag)
        IpsetTestHelper.CheckAndRestoreFlag(RootDir, ListsDir);

        // Select test type
        Console.WriteLine("   [1] Стандартные тесты (HTTP/TLS1.2/TLS1.3 + ping)");
        Console.WriteLine("   [2] DPI тесты (TCP 16-20 KB freeze)");
        var testType = ConsoleMenu.Prompt("Выберите тип теста", "1");

        var winws = Path.Combine(BinDir, "winws.exe");
        var files = StrategyReader.GetStrategyFiles(RootDir);
        if (files.Length == 0)
        {
            ConsoleMenu.WriteError("Стратегии general*.bat не найдены в папке strategies/");
            ConsoleMenu.PauseAny(); return;
        }

        // Select configs
        var selectedConfigs = StrategyTester.SelectConfigs(files);

        // Save winws snapshot
        var winwsSnapshot = WinWsSnapshot.Capture();

        // Save original ipset status for DPI tests
        var originalIpsetStatus = IpsetTestHelper.GetStatus(ListsDir);

        try
        {
            if (testType == "2")
            {
                // ── DPI тесты ──
                ConsoleMenu.WriteStep("Загрузка DPI suite...");
                var dpiTargets = await DpiChecker.GetSuiteAsync();
                if (dpiTargets.Count == 0)
                {
                    ConsoleMenu.WriteError("Suite недоступен");
                    ConsoleMenu.PauseAny(); return;
                }

                var curlPath = Path.Combine(BinDir, "curl.exe");
                if (!File.Exists(curlPath)) curlPath = "curl.exe";

                // Switch ipset to 'any' for accurate DPI tests
                if (originalIpsetStatus != "any")
                {
                    ConsoleMenu.WriteWarn($"IPSet в режиме '{originalIpsetStatus}'. Переключение в 'any' для точных DPI тестов...");
                    IpsetTestHelper.SwitchToAny(ListsDir);
                    IpsetTestHelper.SetFlag(RootDir);
                }

                var allDpiResults = new List<(string Config, List<DpiTargetResult> Results)>();

                ConsoleMenu.WriteWarn("Тесты займут несколько минут. Пожалуйста, подождите...");

                for (int i = 0; i < selectedConfigs.Length; i++)
                {
                    var file = selectedConfigs[i];
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.WriteLine($"\n  [{i + 1}/{selectedConfigs.Length}] {file.Name}");
                    Console.WriteLine("  " + new string('─', 56));
                    Console.ResetColor();

                    StopZapretForTest();
                    await Task.Delay(500);

                    // Start config via cmd.exe
                    var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe",
                        $"/c \"{file.FullName}\"") { WorkingDirectory = RootDir, WindowStyle = System.Diagnostics.ProcessWindowStyle.Minimized, UseShellExecute = true });
                    await Task.Delay(5000);

                    ConsoleMenu.WriteInfo("Выполнение DPI тестов...");
                    var dpiResults = await DpiChecker.RunSuiteAsync(dpiTargets, curlPath);
                    DpiChecker.PrintResults(dpiResults);
                    allDpiResults.Add((file.Name, dpiResults));

                    StopZapretForTest();
                    if (proc != null && !proc.HasExited) try { proc.Kill(); } catch { }
                    await Task.Delay(500);
                }

                // DPI Analytics
                ConsoleMenu.WriteHeader("DPI АНАЛИТИКА");
                string? bestDpi = null;
                int maxOk = 0;
                foreach (var (config, results) in allDpiResults)
                {
                    int ok = results.SelectMany(r => r.Lines).Count(l => l.Status == "OK");
                    int fail = results.SelectMany(r => r.Lines).Count(l => l.Status == "FAIL");
                    int blocked = results.SelectMany(r => r.Lines).Count(l => l.Status == "LIKELY_BLOCKED");
                    int unsup = results.SelectMany(r => r.Lines).Count(l => l.Status == "UNSUPPORTED");
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"   {config} : OK: {ok}, FAIL: {fail}, UNSUP: {unsup}, BLOCKED: {blocked}");
                    Console.ResetColor();
                    if (ok > maxOk) { maxOk = ok; bestDpi = config; }
                }
                if (bestDpi != null) ConsoleMenu.WriteOk($"Лучший конфиг: {bestDpi}");

                // Save DPI results
                StrategyTester.SaveDpiResults(RootDir, allDpiResults);
            }
            else
            {
                // ── Стандартные тесты ──
                var targets = StrategyTester.LoadTargets(RootDir, Cfg.Diagnostics.CheckTargets);
                if (targets.Count == 0)
                {
                    ConsoleMenu.WriteError("Нет целей для тестирования");
                    ConsoleMenu.PauseAny(); return;
                }

                var allResults = await StrategyTester.RunStandardTestsAsync(
                    RootDir, selectedConfigs, targets, winws);

                // Analytics
                var analytics = StrategyTester.ComputeAnalytics(allResults);
                StrategyTester.PrintAnalytics(analytics);

                // Save results
                StrategyTester.SaveStandardResults(RootDir, allResults, analytics);

                // Offer to install best
                var bestConfig = StrategyTester.GetBestConfig(analytics);
                if (bestConfig != null)
                {
                    var bestFile = selectedConfigs.FirstOrDefault(f => f.Name == bestConfig);
                    if (bestFile != null && ConsoleMenu.Confirm("Установить лучшую стратегию как службу?"))
                    {
                        var gf = GameFilter.Get(UtilsDir);
                        var bArgs = StrategyReader.ParseArgs(bestFile.FullName, BinDir, ListsDir, gf.Tcp, gf.Udp);
                        var wp = Path.Combine(BinDir, "winws.exe");
                        WinServiceManager.Install("zapret", "zapret", "Zapret DPI bypass",
                            $"\"{wp}\" {bArgs}");
                        ConsoleMenu.WriteOk("Служба установлена");
                    }
                }
            }
        }
        finally
        {
            // Restore ipset if it was switched
            if (originalIpsetStatus != "any")
            {
                ConsoleMenu.WriteInfo("Восстановление ipset...");
                IpsetTestHelper.Restore(ListsDir);
                IpsetTestHelper.RemoveFlag(RootDir);
            }

            // Restore winws — restart service if it was running, else restore processes
            StopZapretForTest();
            await RestoreZapretAfterTestAsync(winwsSnapshot);
        }

        ConsoleMenu.PauseAny();
    }

    static void MenuExportReport() => DiagnosticsMenu.ExportReport(RootDir);

    static async Task MenuTgProxy() => await TgProxyMenu.RunAsync(RootDir);




    // ── SETUP WIZARD (mirrors autosetup.ps1 flow) ─────────────────────────────
    static async Task RunSetupAsync(string[]? args = null)
    {
        bool silent = args?.Contains("--silent") == true;
        string? forcedStrategy = null;
        for (int i = 0; i < (args?.Length ?? 0) - 1; i++)
            if (args![i] == "--strategy") { forcedStrategy = args[i + 1]; break; }

        while (true)
        {
        Console.Clear();
        Console.Title = "Zapret Auto-Setup";
        var mgrVer  = GitHubUpdater.ReadManagerVersion(RootDir) ?? Cfg.Project.Version;
        var coreVer = ReadLocalCoreVersion();
        ConsoleMenu.WriteHeader($"ZAPRET AUTO-SETUP");
        ConsoleMenu.WriteInfo($"Manager: v{mgrVer}  |  Zapret Core: {coreVer}");
        ConsoleMenu.WriteInfo($"Рабочая папка: {RootDir}");
        ConsoleMenu.WriteInfo($".NET: {Environment.Version}");

        // ── Фоновая проверка обновлений (оба этапа) ──
        var managerUpdateTask = GitHubUpdater.CheckManagerUpdateAsync(Cfg, RootDir);
        var coreUpdateTask = GitHubUpdater.CheckZapretCoreAsync(Cfg, RootDir);

        // Главное меню
        string mainOpt = "1";
        if (!silent)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  " + new string('═', 54));
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  ГЛАВНОЕ МЕНЮ");
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  " + new string('═', 54));
            Console.ResetColor();
            Console.WriteLine("    [1]  Установить / Обновить конфигурацию");
            Console.WriteLine("    [2]  Удалить zapret");
            Console.WriteLine("    [3]  Переустановить");
            Console.WriteLine("    [4]  Диагностика и отчёт");
            Console.WriteLine("    [5]  Тест стратегий и установка");
            Console.WriteLine("    [6]  Сервисное меню");
            Console.WriteLine("    [7]  Настройки");
            Console.WriteLine("    [0]  Выход");
            Console.WriteLine();
            mainOpt = ConsoleMenu.Prompt("  Выберите (0-7, по умолчанию 1)", "1") ?? "1";
        }

        // ── Этап 1: Проверка обновлений zapret-manager ──
        try
        {
            var (mgrRemote, mgrLocal, mgrDownloadUrl) = await managerUpdateTask;
            if (UpdateChecker.IsNewerVersion(mgrRemote, mgrLocal))
            {
                ConsoleMenu.WriteWarn($"Доступна новая версия zapret-manager: v{mgrRemote} (у вас: v{mgrLocal ?? "не определена"})");
                if (!silent && mgrDownloadUrl != null && ConsoleMenu.Confirm("Обновить zapret-manager?"))
                {
                    var updated = await GitHubUpdater.UpdateManagerAsync(mgrDownloadUrl, RootDir, mgrRemote);
                    if (updated)
                    {
                        ConsoleMenu.WriteOk("Обновление запущено. Приложение будет перезапущено.");
                        Environment.Exit(0);
                        return;
                    }
                }
            }
            else if (mgrRemote != null)
            {
                ConsoleMenu.WriteOk($"Manager актуален: v{mgrLocal}");
            }
        }
        catch (Exception ex) { Logger.Error($"[Program] {ex.GetType().Name}: {ex.Message}"); }

        // ── Этап 2: Проверка обновлений zapret core ──
        try
        {
            var (remote, local) = await coreUpdateTask;
            if (UpdateChecker.IsNewerVersion(remote, local))
            {
                ConsoleMenu.WriteWarn($"Доступна новая версия zapret core: {remote} (у вас: {local ?? "не установлена"})");
                if (!silent && ConsoleMenu.Confirm("Обновить файлы zapret core (bin, strategies, lists)?"))
                {
                    await GitHubUpdater.UpdateZapretCoreFilesAsync(Cfg, RootDir);
                }
            }
            else if (remote != null)
            {
                ConsoleMenu.WriteOk($"Zapret core актуален: {local}");
            }
        }
        catch (Exception ex) { Logger.Error($"[Program] {ex.GetType().Name}: {ex.Message}"); }

        switch (mainOpt.ToLower())
        {
            case "0": return;
            case "2": await RunRemoveAsync(silent); ConsoleMenu.PauseAny(); continue;
            case "3": await RunRemoveAsync(silent: true); break; // continue to install
            case "4": await MenuDiagnostics(); MenuExportReport(); ConsoleMenu.PauseAny(); continue;
            case "5": await RunTestAndInstallAsync(); continue;
            case "6": await RunMenuAsync(); continue;
            case "7": await MenuSettings(); continue;
        }

        // Инициализация пользовательских списков-заглушек
        Directory.CreateDirectory(ListsDir);
        foreach (var e in Cfg.Lists.Files.Where(e => e.User))
        {
            var p = Path.Combine(ListsDir, e.Local);
            if (!File.Exists(p)) ListMerger.WriteUtf8(p, new[] { e.Stub });
        }

        // Проверка файлов
        ConsoleMenu.WriteStep("Проверка необходимых файлов");
        var winwsExe = Path.Combine(BinDir, "winws.exe");
        if (!File.Exists(winwsExe))
        {
            ConsoleMenu.WriteError($"winws.exe не найден в {BinDir}");
            if (!silent) ConsoleMenu.PauseAny();
            return;
        }
        ConsoleMenu.WriteOk("winws.exe найден");
        if (File.Exists(Path.Combine(BinDir, "curl.exe")) || IsInPath("curl.exe"))
            ConsoleMenu.WriteOk("curl.exe найден");
        else
            ConsoleMenu.WriteWarn("curl.exe не найден — HTTP-тесты ограничены");

        // Защита от антивируса
        ConsoleMenu.WriteStep("Защита файлов от антивируса");
        var avResult = AntivirusExcluder.ProtectAndVerify(RootDir, BinDir);
        if (avResult.Level == DiagLevel.Ok)
            ConsoleMenu.WriteOk(avResult.Message);
        else
            ConsoleMenu.WriteWarn(avResult.Message);

        // Копирование TgWsProxy_windows.exe из orig/ в RootDir
        ConsoleMenu.WriteStep("Проверка TgWsProxy");
        var tgPath = TgProxyManager.EnsureInRootDir(RootDir);
        if (tgPath != null)
            ConsoleMenu.WriteOk($"TgWsProxy_windows.exe: {tgPath}");
        else
            ConsoleMenu.WriteInfo("TgWsProxy_windows.exe не найден (TG прокси недоступен)");

        // Конфликтующие службы
        ConsoleMenu.WriteStep("Проверка конфликтующих служб");
        var conflicts = ConflictDetector.FindConflicts(Cfg.Diagnostics.ConflictingServices);
        if (conflicts.Count > 0)
        {
            ConsoleMenu.WriteWarn($"Найдены: {string.Join(", ", conflicts)}");
            if (silent || ConsoleMenu.Confirm("Удалить автоматически?"))
                ConflictDetector.RemoveConflicts(conflicts);
        }
        else ConsoleMenu.WriteOk("Конфликтующих служб не найдено");

        // Game Filter
        if (!silent)
        {
            ConsoleMenu.WriteStep("Настройка Game Filter");
            ConsoleMenu.WriteInfo($"Текущий статус: {GameFilter.StatusLabel(UtilsDir)}");
            Console.WriteLine("   [1] Оставить как есть  [2] Отключить  [3] TCP+UDP  [4] Только TCP  [5] Только UDP");
            switch (ConsoleMenu.Prompt("Выберите (1..5)", "1"))
            {
                case "2": GameFilter.Set(UtilsDir, "disabled"); ConsoleMenu.WriteOk("Game Filter отключён"); break;
                case "3": GameFilter.Set(UtilsDir, "all");      ConsoleMenu.WriteOk("Game Filter: TCP+UDP"); break;
                case "4": GameFilter.Set(UtilsDir, "tcp");      ConsoleMenu.WriteOk("Game Filter: только TCP"); break;
                case "5": GameFilter.Set(UtilsDir, "udp");      ConsoleMenu.WriteOk("Game Filter: только UDP"); break;
                default:  ConsoleMenu.WriteInfo("Game Filter не изменён"); break;
            }
        }

        // IPSet фильтр
        if (!silent)
        {
            ConsoleMenu.WriteStep("Настройка IPSet фильтра");
            var currentIpset = GetIpsetStatus();
            ConsoleMenu.WriteInfo($"Текущий режим: {currentIpset}");
            Console.WriteLine("   [1] any    — обход всех IP (рекомендуется)");
            Console.WriteLine("   [2] loaded — только IP из списка ipset-all.txt");
            Console.WriteLine("   [3] none   — отключить IPSet фильтр");
            Console.WriteLine("   [4] Оставить как есть");
            var ipsetChoice = ConsoleMenu.Prompt("Выберите (1..4, по умолчанию 1)", "1");
            var listFile   = Path.Combine(ListsDir, "ipset-all.txt");
            var backupFile = listFile + ".backup";
            switch (ipsetChoice)
            {
                case "1": // any
                    if (File.Exists(listFile) && !File.ReadAllText(listFile).Trim().Equals(""))
                    {
                        if (!File.Exists(backupFile))
                            File.Copy(listFile, backupFile, true);
                    }
                    Directory.CreateDirectory(ListsDir);
                    File.WriteAllText(listFile, "\r\n");
                    ConsoleMenu.WriteOk("IPSet: any (обход всех IP)");
                    break;
                case "2": // loaded
                    if (File.Exists(backupFile))
                    {
                        File.Copy(backupFile, listFile, true);
                        ConsoleMenu.WriteOk("IPSet: loaded (из списка)");
                    }
                    else
                        ConsoleMenu.WriteInfo("IPSet: список будет загружен далее");
                    break;
                case "3": // none
                    if (File.Exists(listFile) && !File.ReadAllText(listFile).Contains("203.0.113.113"))
                    {
                        if (!File.Exists(backupFile))
                            File.Copy(listFile, backupFile, true);
                    }
                    Directory.CreateDirectory(ListsDir);
                    File.WriteAllText(listFile, "203.0.113.113/32\r\n");
                    ConsoleMenu.WriteOk("IPSet: none (отключён)");
                    break;
                default:
                    ConsoleMenu.WriteInfo("IPSet фильтр не изменён");
                    break;
            }
        }

        // Режим обновлений
        if (!silent)
        {
            ConsoleMenu.WriteStep("Настройка режима обновлений");
            var currentMode = UpdateChecker.GetUpdateMode(RootDir);
            ConsoleMenu.WriteInfo($"Текущий режим: {(currentMode == "auto" ? "автоматический" : "ручной")}");
            Console.WriteLine("   [1] Ручной   — уведомление о новой версии, обновление через меню (по умолчанию)");
            Console.WriteLine("   [2] Авто     — автоматическое обновление при обнаружении новой версии");
            Console.WriteLine("   [3] Оставить как есть");
            var updChoice = ConsoleMenu.Prompt("Выберите (1..3, по умолчанию 1)", "1");
            switch (updChoice)
            {
                case "1":
                    UpdateChecker.SetUpdateMode(RootDir, "manual");
                    ConsoleMenu.WriteOk("Режим обновлений: ручной");
                    break;
                case "2":
                    UpdateChecker.SetUpdateMode(RootDir, "auto");
                    ConsoleMenu.WriteOk("Режим обновлений: автоматический");
                    break;
                default:
                    ConsoleMenu.WriteInfo("Режим обновлений не изменён");
                    break;
            }

            // Убедимся что файл check_updates.enabled создан
            var flagPath = Path.Combine(UtilsDir, "check_updates.enabled");
            if (!File.Exists(flagPath))
                File.WriteAllText(flagPath, "ВКЛЮЧЕНО");
        }

        // TCP timestamps
        ConsoleMenu.WriteStep("Включение TCP timestamps");
        RunNetsh("interface tcp set global timestamps=enabled");
        ConsoleMenu.WriteOk("TCP timestamps включены");

        // Загрузка списков с GitHub
        ConsoleMenu.WriteStep("Загрузка списков IP и доменов с GitHub");
        await ListDownloader.DownloadAllAsync(
            Cfg.Lists.Files, ListsDir,
            "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main",
            (msg, ok) => { if (ok) ConsoleMenu.WriteOk(msg); else ConsoleMenu.WriteWarn(msg); });

        // Очистка списков
        ConsoleMenu.WriteStep("Очистка списков (дубликаты, пустые строки, невалидные IP)");
        ListRepairer.RepairAll(ListsDir, Cfg.Features.RemoveCidrOverlap);
        ConsoleMenu.WriteOk("Списки очищены");

        // Обновление hosts
        ConsoleMenu.WriteStep("Обновление файла hosts");
        var hostsUrl = Cfg.Repositories.ZapretCore.HostsService ?? "";
        if (!string.IsNullOrWhiteSpace(hostsUrl))
        {
            var hostsNeedsUpdate = await HostsUpdater.CheckAndUpdate(hostsUrl);
            if (hostsNeedsUpdate)
                ConsoleMenu.WriteWarn("Файл hosts требует обновления — открыт в Блокноте для ручного слияния");
            else
                ConsoleMenu.WriteOk("Файл hosts актуален");
        }
        else ConsoleMenu.WriteInfo("URL hosts не настроен в config.json — пропуск");

        // Синхронизация publish/lists с основной lists/
        SyncPublishLists();

        // Проверка доступности БЕЗ обхода
        ConsoleMenu.WriteStep("Проверка доступности сайтов БЕЗ обхода");
        StopZapretForTest();
        var preResults = await AccessChecker.CheckAllAsync(Cfg.Diagnostics.CheckTargets);
        bool allOk = preResults.All(r => r.Reachable);
        foreach (var r in preResults)
        {
            if (r.Reachable) ConsoleMenu.WriteOk($"{r.Name}: доступен");
            else             ConsoleMenu.WriteWarn($"{r.Name}: недоступен (нужен обход)");
        }
        if (allOk && !silent)
        {
            ConsoleMenu.WriteOk("Все сайты доступны без обхода!");
            if (!ConsoleMenu.Confirm("Всё равно продолжить установку?")) { ConsoleMenu.PauseAny(); return; }
        }

        // Выбор стратегии
        var batFiles = StrategyReader.GetStrategyFiles(RootDir);
        if (batFiles.Length == 0)
        {
            ConsoleMenu.WriteError("Не найдены файлы general*.bat в папке strategies/");
            if (!silent) ConsoleMenu.PauseAny();
            return;
        }

        string? chosenBat = null;
        if (!string.IsNullOrWhiteSpace(forcedStrategy))
            chosenBat = batFiles.FirstOrDefault(f =>
                f.Name.Contains(forcedStrategy, StringComparison.OrdinalIgnoreCase))?.FullName;

        if (chosenBat == null && !silent)
        {
            Console.WriteLine();
            ConsoleMenu.WriteSeparator();
            ConsoleMenu.WriteInfo($"Найдено конфигов: {batFiles.Length}");
            ConsoleMenu.WriteSeparator();
            Console.WriteLine("  [1]  Тест стратегий — автоматический выбор лучшего конфига");
            Console.WriteLine("  [2]  Ручной выбор конфига из списка");
            Console.WriteLine("  [3]  Отмена");
            Console.WriteLine();
            var modeChoice = ConsoleMenu.Prompt("  Введите номер (1/2/3)", "1");
            switch (modeChoice)
            {
                case "1":
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.WriteLine("  ╔═══════════════════════════════════════════╗");
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("  ║  ТЕСТ СТРАТЕГИЙ                           ║");
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("  ║                                           ║");
                    Console.WriteLine("  ║  [1] Стандартный — HTTP/TLS/ping тесты    ║");
                    Console.WriteLine("  ║      (доступность сайтов, score)          ║");
                    Console.WriteLine("  ║                                           ║");
                    Console.WriteLine("  ║  [2] DPI тест — TCP 16-20 KB freeze       ║");
                    Console.WriteLine("  ║      (curl payload, паттерн блокировки)   ║");
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.WriteLine("  ╚═══════════════════════════════════════════╝");
                    Console.ResetColor();
                    Console.WriteLine();
                    var testMode = ConsoleMenu.Prompt("  Выберите режим (1/2)", "1");

                    if (testMode == "1")
                    {
                        // Стандартный тест
                        var targets = StrategyTester.LoadTargets(RootDir, Cfg.Diagnostics.CheckTargets);
                        var selectedConfigs = StrategyTester.SelectConfigs(batFiles);
                        var allResults = await StrategyTester.RunStandardTestsAsync(RootDir, selectedConfigs, targets, winwsExe);
                        var analytics = StrategyTester.ComputeAnalytics(allResults);
                        StrategyTester.PrintAnalytics(analytics);
                        StrategyTester.SaveStandardResults(RootDir, allResults, analytics);
                        var bestName = StrategyTester.GetBestConfig(analytics);
                        if (bestName != null)
                        {
                            var bestFile = batFiles.FirstOrDefault(f => f.Name == bestName);
                            if (bestFile != null) { chosenBat = bestFile.FullName; ConsoleMenu.WriteOk($"Лучшая: {bestName}"); }
                        }
                    }
                    else if (testMode == "2")
                    {
                        // DPI тест
                        var selectedConfigs = StrategyTester.SelectConfigs(batFiles);
                        
                        // Backup ipset and switch to "any" for testing
                        IpsetTestHelper.SwitchToAny(ListsDir);
                        IpsetTestHelper.SetFlag(RootDir);
                        var winwsSnapshot = WinWsSnapshot.Capture();

                        // Load DPI suite
                        ConsoleMenu.WriteStep("Загрузка DPI suite...");
                        var dpiTargets = await DpiChecker.GetSuiteAsync();
                        if (dpiTargets.Count == 0)
                        {
                            ConsoleMenu.WriteError("Не удалось загрузить DPI suite");
                            IpsetTestHelper.Restore(ListsDir);
                            IpsetTestHelper.RemoveFlag(RootDir);
                            break;
                        }
                        ConsoleMenu.WriteOk($"Загружено {dpiTargets.Count} целей");

                        var curlPath = File.Exists(Path.Combine(BinDir, "curl.exe")) 
                            ? Path.Combine(BinDir, "curl.exe") : "curl.exe";

                        var allDpiResults = new List<(string Config, List<DpiTargetResult> Results)>();
                        ConsoleMenu.WriteWarn("DPI тесты займут несколько минут...");

                        for (int i = 0; i < selectedConfigs.Length; i++)
                        {
                            var file = selectedConfigs[i];
                            Console.ForegroundColor = ConsoleColor.DarkCyan;
                            Console.WriteLine($"\n  [{i + 1}/{selectedConfigs.Length}] {file.Name}");
                            Console.WriteLine("  " + new string('─', 56));
                            Console.ResetColor();

                            StopZapretForTest();
                            await Task.Delay(500);

                            // Launch strategy via bat file
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe",
                                $"/c \"{file.FullName}\"") { WorkingDirectory = RootDir, WindowStyle = System.Diagnostics.ProcessWindowStyle.Minimized, UseShellExecute = true });
                            await Task.Delay(5000);

                            var dpiResults = await DpiChecker.RunSuiteAsync(dpiTargets, curlPath);
                            allDpiResults.Add((file.Name, dpiResults));

                            // Детальный вывод результатов для этого конфига
                            DpiChecker.PrintResults(dpiResults);

                            StopZapretForTest();
                            await Task.Delay(500);
                        }

                        // Restore
                        IpsetTestHelper.Restore(ListsDir);
                        IpsetTestHelper.RemoveFlag(RootDir);
                        await RestoreZapretAfterTestAsync(winwsSnapshot);

                        // Аналитика и выбор лучшего
                        var bestDpiConfig = DpiChecker.PrintDpiAnalytics(allDpiResults);
                        DpiChecker.SaveDpiResults(RootDir, allDpiResults, bestDpiConfig);

                        if (bestDpiConfig != null)
                        {
                            var bestFile = batFiles.FirstOrDefault(f => f.Name == bestDpiConfig);
                            if (bestFile != null) { chosenBat = bestFile.FullName; ConsoleMenu.WriteOk($"Лучшая (DPI): {bestDpiConfig}"); }
                        }
                    }
                    break;
                case "2":
                    Console.WriteLine();
                    for (int i = 0; i < batFiles.Length; i++) Console.WriteLine($"   [{i + 1}] {batFiles[i].Name}");
                    var pick = ConsoleMenu.Prompt("  Введите номер конфига", "1");
                    if (int.TryParse(pick, out var pidx) && pidx >= 1 && pidx <= batFiles.Length)
                        chosenBat = batFiles[pidx - 1].FullName;
                    break;
                default:
                    ConsoleMenu.WriteInfo("Установка отменена");
                    ConsoleMenu.PauseAny(); return;
            }
        }

        if (chosenBat == null)
        {
            ConsoleMenu.WriteError("Конфиг не выбран");
            if (!silent) ConsoleMenu.PauseAny();
            return;
        }

        // Установка службы
        try
        {
            var gf2 = GameFilter.Get(UtilsDir);
            var wArgs = StrategyReader.ParseArgs(chosenBat, BinDir, ListsDir, gf2.Tcp, gf2.Udp);
            if (string.IsNullOrWhiteSpace(wArgs))
            {
                ConsoleMenu.WriteError($"Не удалось извлечь аргументы из {Path.GetFileName(chosenBat)}");
                ConsoleMenu.WriteInfo("Попробуйте другой конфиг или проверьте формат bat-файла");
                if (!silent) ConsoleMenu.PauseAny();
                return;
            }
            ConsoleMenu.WriteStep($"Установка службы Windows: {Path.GetFileName(chosenBat)}");
            Logger.Info($"Аргументы winws: {wArgs}");
            var svcOk = WinServiceManager.Install("zapret", "zapret", "Zapret DPI bypass", $"\"{winwsExe}\" {wArgs}");

            // Сохраняем имя стратегии в реестр
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"System\CurrentControlSet\Services\zapret");
                key?.SetValue("zapret-discord-youtube", Path.GetFileNameWithoutExtension(chosenBat));
            }
            catch (Exception ex) { Logger.Error($"[Program] {ex.GetType().Name}: {ex.Message}"); }

            if (svcOk)
            {
                ConsoleMenu.WriteOk($"Служба 'zapret' установлена и запущена: {Path.GetFileName(chosenBat)}");
            }
            else
            {
                // Служба создана, но не запустилась
                var state = WinServiceManager.GetState("zapret");
                if (state == WinServiceManager.ServiceState.NotInstalled)
                {
                    ConsoleMenu.WriteError("Не удалось создать службу. Проверьте права администратора.");
                }
                else
                {
                    ConsoleMenu.WriteError("Служба создана, но НЕ запустилась (winws.exe упал)");
                    ConsoleMenu.WriteInfo("Возможные причины:");
                    ConsoleMenu.WriteInfo("  • Антивирус блокирует winws.exe — добавьте в исключения");
                    ConsoleMenu.WriteInfo("  • Файлы WinDivert.dll / WinDivert64.sys отсутствуют в bin/");
                    ConsoleMenu.WriteInfo("  • Порты уже заняты другим экземпляром");
                    ConsoleMenu.WriteInfo("  • Драйвер WinDivert не может загрузиться");

                    // Проверяем конкретные файлы
                    if (!File.Exists(Path.Combine(BinDir, "WinDivert64.sys")))
                        ConsoleMenu.WriteError("  ✗ WinDivert64.sys НЕ найден в bin/");
                    if (!File.Exists(Path.Combine(BinDir, "WinDivert.dll")))
                        ConsoleMenu.WriteError("  ✗ WinDivert.dll НЕ найден в bin/");
                    if (!File.Exists(Path.Combine(BinDir, "cygwin1.dll")))
                        ConsoleMenu.WriteError("  ✗ cygwin1.dll НЕ найден в bin/");

                    // Попробовать запустить повторно
                    if (!silent && ConsoleMenu.Confirm("Попробовать перезапустить службу?"))
                    {
                        WinServiceManager.Stop("zapret");
                        await Task.Delay(1000);
                        var retryOk = WinServiceManager.Start("zapret");
                        await Task.Delay(2000);
                        var retryState = WinServiceManager.GetState("zapret");
                        if (retryState == WinServiceManager.ServiceState.Running)
                            ConsoleMenu.WriteOk("Служба запущена после повторного старта!");
                        else
                            ConsoleMenu.WriteError("Служба снова упала. Запустите диагностику (п.10 в сервисном меню).");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ConsoleMenu.WriteError($"Ошибка установки службы: {ex.Message}");
            Logger.Error($"Service install failed: {ex}");
            if (!silent) ConsoleMenu.PauseAny();
            return;
        }

        // Пост-проверка
        if (!silent)
        {
            ConsoleMenu.WriteStep("Проверка доступности С обходом (ждём 5 сек...)");
            await Task.Delay(5000);
            var post = await AccessChecker.CheckAllAsync(Cfg.Diagnostics.CheckTargets);
            foreach (var r in post)
            {
                if (r.Reachable) ConsoleMenu.WriteOk($"{r.Name}: {r.Detail}");
                else             ConsoleMenu.WriteWarn($"{r.Name}: всё ещё недоступен");
            }
            int okCnt = post.Count(r => r.Reachable);
            Console.WriteLine();
            if (okCnt > post.Count / 2)  ConsoleMenu.WriteOk($"ZAPRET РАБОТАЕТ! {okCnt}/{post.Count} ресурсов доступны");
            else if (okCnt > 0)           ConsoleMenu.WriteWarn($"Частично: {okCnt}/{post.Count}. Попробуйте другую стратегию.");
            else                          ConsoleMenu.WriteError("Ни один ресурс не работает. Запустите --menu → 11 (тест стратегий).");

            ToastNotifier.Show("Zapret", $"Служба установлена: {Path.GetFileNameWithoutExtension(chosenBat)}");
            ConsoleMenu.PauseAny("Нажмите любую клавишу для возврата в меню...");
        }
        Logger.Info("=== Установка завершена ===");
        if (silent) return; // в silent-режиме выходим
        } // end while(true)
    }

    static async Task RunRemoveAsync(bool silent = false)
    {
        Console.Clear();
        ConsoleMenu.WriteHeader("УДАЛЕНИЕ ZAPRET");

        // Подтверждение перед удалением
        if (!silent)
        {
            ConsoleMenu.WriteWarn("Будут удалены ВСЕ службы zapret, WinDivert, WinDivert14.");
            ConsoleMenu.WriteInfo("Ваши списки в папке lists/ сохранены.");
            if (!ConsoleMenu.Confirm("Продолжить удаление?"))
            {
                ConsoleMenu.WriteInfo("Удаление отменено");
                ConsoleMenu.PauseAny();
                return;
            }
        }

        ProcessManager.KillAll();
        foreach (var svc in new[] { "zapret", "WinDivert", "WinDivert14" })
        {
            WinServiceManager.Stop(svc);
            if (WinServiceManager.Remove(svc)) ConsoleMenu.WriteOk($"Служба удалена: {svc}");
            else ConsoleMenu.WriteInfo($"{svc}: не установлена");
        }
        ConsoleMenu.WriteOk("Готово. Списки в папке lists/ сохранены.");
        if (!silent) ConsoleMenu.PauseAny();
        await Task.CompletedTask;
    }

    static async Task RunTestAndInstallAsync()
    {
        Console.Clear();
        ConsoleMenu.WriteHeader("ТЕСТ СТРАТЕГИЙ И УСТАНОВКА");
        var winwsExe = Path.Combine(BinDir, "winws.exe");
        var files = StrategyReader.GetStrategyFiles(RootDir);
        if (files.Length == 0)
        {
            ConsoleMenu.WriteError("Стратегии general*.bat не найдены");
            ConsoleMenu.PauseAny(); return;
        }

        // Выбор режима тестирования
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("  ╔═══════════════════════════════════════════╗");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  ║  ТЕСТ СТРАТЕГИЙ                           ║");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  ║                                           ║");
        Console.WriteLine("  ║  [1] Стандартный — HTTP/TLS/ping тесты    ║");
        Console.WriteLine("  ║      (доступность сайтов, score)          ║");
        Console.WriteLine("  ║                                           ║");
        Console.WriteLine("  ║  [2] DPI тест — TCP 16-20 KB freeze       ║");
        Console.WriteLine("  ║      (curl payload, паттерн блокировки)   ║");
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("  ╚═══════════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();
        var testMode = ConsoleMenu.Prompt("  Выберите режим (1/2)", "1");

        string? bestConfig = null;

        if (testMode == "1")
        {
            // Стандартный тест
            var targets = StrategyTester.LoadTargets(RootDir, Cfg.Diagnostics.CheckTargets);
            var selectedConfigs = StrategyTester.SelectConfigs(files);
            var allResults = await StrategyTester.RunStandardTestsAsync(RootDir, selectedConfigs, targets, winwsExe);
            var analytics = StrategyTester.ComputeAnalytics(allResults);
            StrategyTester.PrintAnalytics(analytics);
            StrategyTester.SaveStandardResults(RootDir, allResults, analytics);
            bestConfig = StrategyTester.GetBestConfig(analytics);
        }
        else if (testMode == "2")
        {
            // DPI тест
            var selectedConfigs = StrategyTester.SelectConfigs(files);

            IpsetTestHelper.SwitchToAny(ListsDir);
            IpsetTestHelper.SetFlag(RootDir);
            var winwsSnapshot = WinWsSnapshot.Capture();

            ConsoleMenu.WriteStep("Загрузка DPI suite...");
            var dpiTargets = await DpiChecker.GetSuiteAsync();
            if (dpiTargets.Count == 0)
            {
                ConsoleMenu.WriteError("Не удалось загрузить DPI suite");
                IpsetTestHelper.Restore(ListsDir);
                IpsetTestHelper.RemoveFlag(RootDir);
                ConsoleMenu.PauseAny(); return;
            }
            ConsoleMenu.WriteOk($"Загружено {dpiTargets.Count} целей");

            var curlPath = File.Exists(Path.Combine(BinDir, "curl.exe"))
                ? Path.Combine(BinDir, "curl.exe") : "curl.exe";

            var allDpiResults = new List<(string Config, List<DpiTargetResult> Results)>();
            ConsoleMenu.WriteWarn("DPI тесты займут несколько минут...");

            for (int i = 0; i < selectedConfigs.Length; i++)
            {
                var file = selectedConfigs[i];
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine($"\n  [{i + 1}/{selectedConfigs.Length}] {file.Name}");
                Console.WriteLine("  " + new string('─', 56));
                Console.ResetColor();

                StopZapretForTest();
                await Task.Delay(500);

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe",
                    $"/c \"{file.FullName}\"") { WorkingDirectory = RootDir, WindowStyle = System.Diagnostics.ProcessWindowStyle.Minimized, UseShellExecute = true });
                await Task.Delay(5000);

                var dpiResults = await DpiChecker.RunSuiteAsync(dpiTargets, curlPath);
                allDpiResults.Add((file.Name, dpiResults));

                // Детальный вывод результатов для этого конфига
                DpiChecker.PrintResults(dpiResults);

                StopZapretForTest();
                await Task.Delay(500);
            }

            IpsetTestHelper.Restore(ListsDir);
            IpsetTestHelper.RemoveFlag(RootDir);
            await RestoreZapretAfterTestAsync(winwsSnapshot);

            // Аналитика и выбор лучшего
            bestConfig = DpiChecker.PrintDpiAnalytics(allDpiResults);
            DpiChecker.SaveDpiResults(RootDir, allDpiResults, bestConfig);
        }
        else
        {
            ConsoleMenu.WriteInfo("Отменено");
            ConsoleMenu.PauseAny(); return;
        }

        // Установка лучшей стратегии
        if (bestConfig != null)
        {
            var bestFile = files.FirstOrDefault(f => f.Name == bestConfig);
            if (bestFile != null)
            {
                ConsoleMenu.WriteOk($"Лучшая стратегия: {bestConfig}");
                if (ConsoleMenu.Confirm("Установить как службу zapret?"))
                {
                    ConsoleMenu.WriteStep($"Установка службы: {bestConfig}");
                    var gf = GameFilter.Get(UtilsDir);
                    var wa = StrategyReader.ParseArgs(bestFile.FullName, BinDir, ListsDir, gf.Tcp, gf.Udp);
                    WinServiceManager.Install("zapret", "zapret", "Zapret DPI bypass", $"\"{winwsExe}\" {wa}");
                    ConsoleMenu.WriteOk("Служба zapret установлена!");
                    await Task.Delay(5000);
                    var post = await AccessChecker.CheckAllAsync(Cfg.Diagnostics.CheckTargets);
                    foreach (var r in post)
                    {
                        if (r.Reachable) ConsoleMenu.WriteOk($"{r.Name}: {r.Detail}");
                        else             ConsoleMenu.WriteWarn($"{r.Name}: недоступен");
                    }
                }
            }
        }
        else ConsoleMenu.WriteInfo("Лучшая стратегия не определена");
        ConsoleMenu.PauseAny();
    }



    static bool IsInPath(string exe)
    {
        try
        {
            var r = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("where", exe)
                { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
            r?.WaitForExit(1000);
            return r?.ExitCode == 0;
        }
        catch (Exception ex) { Logger.Error($"[Program] {ex.GetType().Name}: {ex.Message}"); return false; }
    }

    // ── SERVICE HEALTH ────────────────────────────────────────────────────────
    static async Task CheckServiceHealth()
    {
        try
        {
            var state = WinServiceManager.GetState("zapret");
            if (state == WinServiceManager.ServiceState.NotInstalled) return;

            var (isHealthy, message) = WinServiceManager.VerifyServiceHealth("zapret", BinDir);
            if (isHealthy) return;

            // ImagePath points to wrong directory
            var imagePath = WinServiceManager.GetImagePath("zapret");
            if (imagePath != null && !imagePath.Contains(BinDir, StringComparison.OrdinalIgnoreCase))
            {
                ConsoleMenu.WriteWarn($"Служба zapret указывает на другую папку.");
                ConsoleMenu.WriteInfo($"  Текущий путь: {imagePath[..Math.Min(80, imagePath.Length)]}...");
                ConsoleMenu.WriteInfo($"  Ожидаемый:    {BinDir}\\winws.exe");
                if (ConsoleMenu.Confirm("Исправить путь службы?"))
                {
                    // Extract args from old ImagePath, replace exe path
                    var winws = Path.Combine(BinDir, "winws.exe");
                    var oldArgs = ExtractArgsFromImagePath(imagePath);
                    var newBinPath = $"\"{winws}\" {oldArgs}";
                    if (WinServiceManager.RepairBinPath("zapret", newBinPath))
                    {
                        ConsoleMenu.WriteOk("Путь службы исправлен");
                        // Restart service with new path
                        WinServiceManager.Stop("zapret");
                        await Task.Delay(1000);
                        WinServiceManager.Start("zapret");
                        await Task.Delay(2000);
                        if (WinServiceManager.GetState("zapret") == WinServiceManager.ServiceState.Running)
                            ConsoleMenu.WriteOk("Служба перезапущена");
                        else
                            ConsoleMenu.WriteWarn("Служба не запустилась. Переустановите через меню.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"CheckServiceHealth: {ex.Message}");
        }
    }

    static string ExtractArgsFromImagePath(string imagePath)
    {
        // ImagePath format: "C:\...\winws.exe" --arg1 --arg2 ...
        // or: C:\...\winws.exe --arg1 --arg2 ...
        var idx = imagePath.IndexOf("winws.exe", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "";
        idx += "winws.exe".Length;
        if (idx < imagePath.Length && imagePath[idx] == '"') idx++;
        return imagePath[idx..].Trim();
    }

    // ── HELPERS ───────────────────────────────────────────────────────────────
    static string GetCurrentStrategy()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"System\CurrentControlSet\Services\zapret");
            if (key == null) return "не установлена";

            // Try the explicit strategy value first
            var strategyVal = key.GetValue("zapret-discord-youtube")?.ToString();
            if (!string.IsNullOrEmpty(strategyVal)) return strategyVal;

            // Fallback: extract strategy from ImagePath
            var imagePath = key.GetValue("ImagePath")?.ToString();
            if (!string.IsNullOrEmpty(imagePath))
            {
                // Service exists but strategy name wasn't saved (e.g. installed by original service.bat)
                // Try to find which .bat file matches the args pattern
                var strategiesDir = Path.Combine(RootDir, "strategies");
                if (Directory.Exists(strategiesDir))
                {
                    var batFiles = Directory.GetFiles(strategiesDir, "general*.bat");
                    foreach (var bat in batFiles)
                    {
                        var batName = Path.GetFileNameWithoutExtension(bat);
                        // Check if any identifying markers from the bat are in ImagePath
                        if (imagePath.Length > 50) // Service is installed with real args
                            return $"{batName} (определено по ImagePath)";
                    }
                }
                return "установлена (имя не записано)";
            }

            return "не установлена";
        }
        catch (Exception ex) { Logger.Error($"[Program] {ex.GetType().Name}: {ex.Message}"); return "?"; }
    }

    static string GetIpsetStatus()
    {
        var f = Path.Combine(ListsDir, "ipset-all.txt");
        if (!File.Exists(f)) return "none";
        var lines = File.ReadAllLines(f).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        if (lines.Length == 0) return "any";
        if (lines.Any(l => l.Trim() == "203.0.113.113/32")) return "none";
        return "loaded";
    }

    static bool IsTgProxyRunning()
        => TgProxyManager.IsRunning();

    static void RunNetsh(string args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("netsh", args)
                { CreateNoWindow = true, UseShellExecute = false };
            System.Diagnostics.Process.Start(psi)?.WaitForExit(3000);
        }
        catch (Exception ex) { Logger.Error($"[Program] {ex.GetType().Name}: {ex.Message}"); }
    }

    /// <summary>
    /// Walk up from exe directory to find the root that contains strategies/ folder.
    /// Falls back to exe directory if not found.
    /// </summary>
    static string DetectRootDir()
    {
        var dir = AppContext.BaseDirectory.TrimEnd('\\', '/');
        var candidate = dir;
        for (int i = 0; i < 5; i++)
        {
            if (Directory.Exists(Path.Combine(candidate, "strategies")))
                return candidate;
            var parent = Path.GetDirectoryName(candidate);
            if (parent == null || parent == candidate) break;
            candidate = parent;
        }
        // Not found via walk-up — use exe dir (strategies/ should be copied there by build)
        return dir;
    }

    static string ReadLocalCoreVersion()
    {
        var vf = Path.Combine(BinDir, "version.txt");
        if (File.Exists(vf))
        {
            var ver = File.ReadAllText(vf).Trim();
            if (!string.IsNullOrEmpty(ver)) return ver;
        }
        return "не установлен";
    }

    static FileInfo[] ParseSelection(string input, FileInfo[] files)
    {
        var result = new List<FileInfo>();
        foreach (var part in input.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part.Trim(), out var n) && n >= 1 && n <= files.Length)
                result.Add(files[n - 1]);
        }
        return result.ToArray();
    }

    /// <summary>Sync lists/ → publish/lists/ so the publish directory stays up to date.</summary>
    static void SyncPublishLists()
    {
        var publishLists = Path.Combine(RootDir, "publish", "lists");
        if (!Directory.Exists(publishLists)) return; // publish/ doesn't exist, skip

        try
        {
            foreach (var file in Directory.EnumerateFiles(ListsDir, "*.txt"))
            {
                var dest = Path.Combine(publishLists, Path.GetFileName(file));
                File.Copy(file, dest, overwrite: true);
            }
            Logger.Info("publish/lists/ синхронизирован с lists/");
        }
        catch (Exception ex) { Logger.Warn($"Синхронизация publish/lists не удалась: {ex.Message}"); }
    }
    // ── TEST HELPERS: Service-aware stop/restore ────────────────────────────
    /// <summary>Stop zapret service and kill winws.exe before running a test.</summary>
    static void StopZapretForTest()
    {
        var state = WinServiceManager.GetState("zapret");
        if (state == WinServiceManager.ServiceState.Running)
            WinServiceManager.Stop("zapret");
        ProcessManager.KillAll();
    }

    /// <summary>Restore zapret after test: restart service if installed, else restore processes.</summary>
    static async Task RestoreZapretAfterTestAsync(List<WinWsSnapshot.WinWsInstance> snapshot)
    {
        ProcessManager.KillAll();
        var state = WinServiceManager.GetState("zapret");
        if (state == WinServiceManager.ServiceState.Stopped)
        {
            ConsoleMenu.WriteInfo("Перезапуск службы zapret...");
            WinServiceManager.Start("zapret");
            await Task.Delay(2000);
            var newState = WinServiceManager.GetState("zapret");
            if (newState == WinServiceManager.ServiceState.Running)
                ConsoleMenu.WriteOk("Служба zapret перезапущена");
            else
                ConsoleMenu.WriteWarn("Служба не запустилась. Запустите вручную через п.3");
        }
        else if (snapshot.Count > 0)
        {
            WinWsSnapshot.Restore(snapshot);
        }
    }

    // ── ISP DETECTION MENU ────────────────────────────────────────────────────
    static async Task MenuIspDetect() => await AdvancedMenu.IspDetectAsync(RootDir);

    // ── DOMAIN MANAGEMENT MENU ────────────────────────────────────────────────
    static void MenuDomains() => AdvancedMenu.Domains(ListsDir);

    // ── NIC SELECTOR MENU ─────────────────────────────────────────────────────
    static void MenuNicSelector() => AdvancedMenu.NicSelector(RootDir);

    // ── SETTINGS EXPORT/IMPORT MENU ───────────────────────────────────────────
    static void MenuSettingsExport() => AdvancedMenu.SettingsExport(RootDir, ListsDir);

    // ── SPEED TEST MENU ───────────────────────────────────────────────────────
    static async Task MenuSpeedTest() => await AdvancedMenu.SpeedTestAsync(StopZapretForTest);

    // ── STRATEGY EDITOR MENU ──────────────────────────────────────────────────
    static void MenuStrategyEditor() => AdvancedMenu.StrategyEditor(RootDir, BinDir, ListsDir);

    // ── WATCHDOG MENU ─────────────────────────────────────────────────────────
    static void MenuWatchdog() => AdvancedMenu.Watchdog(ref _watchdog, RootDir, Cfg);

    // ── TRAY MODE (отдельный процесс) ──────────────────────────────────────────
    static async Task RunTrayMode()
    {
        // Hide console window completely
        TrayManager.HideConsole();
        Console.Title = "Zapret Tray";
        Logger.Info("Tray-процесс запущен");

        _tray = new TrayManager(RootDir, Cfg);
        _tray.Start();

        // Watchdog в tray-процессе тоже
        if (Watchdog.IsEnabledFlag(RootDir))
        {
            _watchdog = new Watchdog(RootDir, Cfg);
            _watchdog.Start();
        }

        // Keep alive forever — tray STA thread + this wait
        var exitEvent = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; exitEvent.Set(); };
        exitEvent.Wait();

        _watchdog?.Dispose();
        _tray.Stop();
        Logger.Dispose();
    }

    /// <summary>
    /// Spawn a separate tray process if not already running.
    /// </summary>
    static void EnsureTrayProcess()
    {
        try
        {
            // Check if tray process already exists
            var current = System.Diagnostics.Process.GetCurrentProcess();
            var trayProcs = System.Diagnostics.Process.GetProcessesByName(current.ProcessName)
                .Where(p => p.Id != current.Id)
                .ToList();

            // Check by window title
            var hasTray = trayProcs.Any(p =>
            {
                try { return p.MainWindowTitle == "Zapret Tray" || p.MainWindowTitle == ""; }
                catch (Exception ex) { Logger.Error($"[Program] {ex.GetType().Name}: {ex.Message}"); return false; }
            });

            if (trayProcs.Count > 0)
            {
                Logger.Info("Tray-процесс уже запущен");
                return;
            }

            var exePath = Environment.ProcessPath
                ?? System.Reflection.Assembly.GetEntryAssembly()?.Location
                ?? "zapret-manager.exe";

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "--tray",
                WorkingDirectory = RootDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };

            System.Diagnostics.Process.Start(psi);
            Logger.Info("Tray-процесс запущен");
        }
        catch (Exception ex)
        {
            Logger.Warn($"Не удалось запустить tray: {ex.Message}");
        }
    }

    /// <summary>
    /// Register tray process in Task Scheduler for autostart on login.
    /// </summary>
    static void RegisterTrayAutostart()
    {
        try
        {
            var exePath = Environment.ProcessPath
                ?? System.Reflection.Assembly.GetEntryAssembly()?.Location
                ?? Path.Combine(RootDir, "zapret-manager.exe");

            var taskName = "ZapretManagerTray";
            // schtasks /create — runs on logon
            var args = $"/create /tn \"{taskName}\" /tr \"\\\"{exePath}\\\" --tray\" /sc ONLOGON /rl HIGHEST /f";
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            var proc = System.Diagnostics.Process.Start(psi);
            proc?.WaitForExit(5000);

            if (proc?.ExitCode == 0)
                ConsoleMenu.WriteOk("Автозапуск трея зарегистрирован в Планировщике задач");
            else
                ConsoleMenu.WriteWarn("Не удалось зарегистрировать автозапуск");
        }
        catch (Exception ex)
        {
            ConsoleMenu.WriteError($"Ошибка регистрации: {ex.Message}");
        }
    }

    static void UnregisterTrayAutostart()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = "/delete /tn \"ZapretManagerTray\" /f",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var proc = System.Diagnostics.Process.Start(psi);
            proc?.WaitForExit(5000);
            ConsoleMenu.WriteOk("Автозапуск трея удалён");
        }
        catch (Exception ex) { Logger.Error($"[Program] {ex.GetType().Name}: {ex.Message}"); }
    }

    // ── SETTINGS SUBMENU (Main Menu п.7) ──────────────────────────────────────
    static async Task MenuSettings()
    {
        while (true)
        {
            Console.Clear();
            ConsoleMenu.WriteHeader("НАСТРОЙКИ");

            var gf = GameFilter.StatusLabel(UtilsDir);
            var ipset = GetIpsetStatus();
            var updateMode = UpdateChecker.GetUpdateMode(RootDir) == "auto" ? "авто" : "ручной";
            var updates = File.Exists(Path.Combine(UtilsDir, "check_updates.enabled")) ? "вкл" : "выкл";

            Console.WriteLine();
            Console.WriteLine($"   1. Обновления           [{updates} | {updateMode}]");
            Console.WriteLine($"   2. Игровой фильтр       [{gf}]");
            Console.WriteLine($"   3. IPSet фильтр         [{ipset}]");
            Console.WriteLine( "   4. Бэкап / Восстановление");
            Console.WriteLine( "   5. Профили");
            Console.WriteLine( "   6. Автозапуск трея при старте системы");
            Console.WriteLine();
            Console.WriteLine( "   0. Назад");
            Console.WriteLine();

            var ch = ConsoleMenu.Prompt("Выберите (0-6)", "0");
            switch (ch)
            {
                case "1": MenuToggleUpdates(); break;
                case "2": MenuGameFilter(); break;
                case "3": MenuIpsetSwitch(); break;
                case "4": MenuBackup(); break;
                case "5": MenuProfiles(); break;
                case "6":
                    if (ConsoleMenu.Confirm("Зарегистрировать автозапуск трея в Планировщике задач?"))
                        RegisterTrayAutostart();
                    else
                        UnregisterTrayAutostart();
                    ConsoleMenu.PauseAny();
                    break;
                case "0": return;
            }
        }
    }
}

