using ZapretManager.UI;
using ZapretManager.Service;
using Spectre.Console;

namespace ZapretManager.Menus;

/// <summary>
/// Telegram WS Proxy menu actions (пункт 13).
/// Extracted from Program.cs as part of the ongoing refactor.
/// </summary>
internal static class TgProxyMenu
{
    // ── Main TG Proxy menu loop (п.13) ───────────────────────────────────────

    internal static async Task RunAsync(string rootDir)
    {
        while (true)
        {
            Console.Clear();
            ConsoleMenu.WriteHeader("TG WS PROXY");

            var exePath = TgProxyManager.FindExePath(rootDir);
            if (exePath == null)
            {
                ConsoleMenu.WriteError("TgWsProxy_windows.exe не найден");
                ConsoleMenu.WriteInfo("Скачайте и разместите файл в корне или в orig/");
                ConsoleMenu.PauseAny(); return;
            }

            var settings = TgProxyManager.LoadSettings(rootDir);
            var running  = TgProxyManager.IsRunning();

            AnsiConsole.MarkupLine(running
                ? $"  [green]Статус: Запущен[/]"
                : $"  [dim]Статус: Остановлен[/]");
            Console.WriteLine($"  Порт: {settings.Port}  Secret: {settings.Secret[..Math.Min(8, settings.Secret.Length)]}...");
            Console.WriteLine($"  Путь: {exePath}");
            Console.WriteLine();
            Console.WriteLine("   [1] Запустить прокси");
            Console.WriteLine("   [2] Остановить прокси");
            Console.WriteLine("   [3] Показать ссылку для Telegram");
            Console.WriteLine("   [4] Настройки");
            Console.WriteLine("   [5] Статус / подробности");
            Console.WriteLine("   [0] Назад");

            var ch = ConsoleMenu.Prompt("\n   Выбор", "0");

            switch (ch)
            {
                case "1":
                    if (running)
                    {
                        ConsoleMenu.WriteWarn("Прокси уже запущен");
                    }
                    else
                    {
                        var proc = TgProxyManager.Start(rootDir, settings);
                        await Task.Delay(1500);
                        if (proc != null && !proc.HasExited)
                        {
                            ConsoleMenu.WriteOk($"Прокси запущен (PID: {proc.Id})");
                            Console.WriteLine();
                            ShowLink(settings);
                        }
                        else
                            ConsoleMenu.WriteError("Не удалось запустить процесс прокси");
                    }
                    ConsoleMenu.PauseAny();
                    break;

                case "2":
                    TgProxyManager.Stop();
                    ConsoleMenu.WriteOk("Прокси остановлен");
                    ConsoleMenu.PauseAny();
                    break;

                case "3":
                    ShowLink(settings);
                    ConsoleMenu.PauseAny();
                    break;

                case "4":
                    EditSettings(settings);
                    TgProxyManager.SaveSettings(rootDir, settings);
                    break;

                case "5":
                    ShowStatus(settings, exePath, rootDir);
                    ConsoleMenu.PauseAny();
                    break;

                case "0":
                    return;
            }
        }
    }

    // ── Show Telegram link ───────────────────────────────────────────────────

    private static void ShowLink(TgProxySettings settings)
    {
        var link = TgProxyManager.GenerateLink(settings);
        AnsiConsole.Write(new Rule("[cyan]═══ ССЫЛКА ДЛЯ TELEGRAM ═══[/]").RuleStyle("cyan dim"));
        AnsiConsole.MarkupLine($"\n  [yellow]{Markup.Escape(link)}[/]");
        AnsiConsole.MarkupLine("\n  [dim]Скопируйте ссылку и вставьте в Telegram.[/]");
        AnsiConsole.Write(new Rule().RuleStyle("cyan dim"));
    }

    // ── Show proxy status ────────────────────────────────────────────────────

    private static void ShowStatus(TgProxySettings settings, string exePath, string rootDir)
    {
        Console.Clear();
        AnsiConsole.Write(new Rule("[cyan]═══ СТАТУС TG PROXY ═══[/]").RuleStyle("cyan dim"));

        var running = TgProxyManager.IsRunning();
        AnsiConsole.MarkupLine(running
            ? "  [green]Статус:       Запущен[/]"
            : "  [red]Статус:       Остановлен[/]");

        Console.WriteLine($"  Путь:         {exePath}");
        Console.WriteLine($"  Хост:         {settings.Host}");
        Console.WriteLine($"  Порт:         {settings.Port}");
        Console.WriteLine($"  Secret:       {settings.Secret}");
        Console.WriteLine($"  Fake TLS:     {(string.IsNullOrEmpty(settings.FakeTlsDomain) ? "не задан" : settings.FakeTlsDomain)}");
        Console.WriteLine($"  CF Proxy:     {(settings.CfProxyEnabled ? "включён" : "выключен")}");
        Console.WriteLine($"  Pool size:    {settings.PoolSize}");
        Console.WriteLine($"  Buffer:       {settings.BufKb} KB");
        Console.WriteLine($"  DC IPs:       {string.Join(", ", settings.DcIps)}");
        Console.WriteLine($"  Verbose:      {(settings.Verbose ? "да" : "нет")}");

        var logFile = Path.Combine(rootDir, "logs", "tg-proxy.log");
        if (File.Exists(logFile))
        {
            var fi = new FileInfo(logFile);
            Console.WriteLine($"  Лог:          {logFile} ({fi.Length / 1024} KB)");
        }
        Console.WriteLine();
    }

    // ── Edit proxy settings ──────────────────────────────────────────────────

    private static void EditSettings(TgProxySettings settings)
    {
        Console.Clear();
        AnsiConsole.Write(new Rule("[cyan]═══ НАСТРОЙКИ TG PROXY ═══[/]").RuleStyle("cyan dim"));

        Console.WriteLine($"\n  Текущие настройки:");
        Console.WriteLine($"    [1] Порт:         {settings.Port}");
        Console.WriteLine($"    [2] Secret:       {settings.Secret}");
        Console.WriteLine($"    [3] Fake TLS:     {(string.IsNullOrEmpty(settings.FakeTlsDomain) ? "не задан" : settings.FakeTlsDomain)}");
        Console.WriteLine($"    [4] CF Proxy:     {(settings.CfProxyEnabled ? "вкл" : "выкл")}");
        Console.WriteLine($"    [5] Pool size:    {settings.PoolSize}");
        Console.WriteLine($"    [6] Verbose:      {(settings.Verbose ? "вкл" : "выкл")}");
        Console.WriteLine($"    [7] Сгенерировать secret  (случайный)");
        Console.WriteLine($"    [0] Назад");

        var ch = ConsoleMenu.Prompt("\n  Выбор", "0");

        switch (ch)
        {
            case "1":
                var portStr = ConsoleMenu.Prompt("  Введите порт", settings.Port.ToString());
                if (int.TryParse(portStr, out var port) && port > 0 && port < 65536)
                    settings.Port = port;
                break;
            case "2":
                var sec = ConsoleMenu.Prompt("  Введите secret (32 hex)", settings.Secret);
                if (sec != null && sec.Length == 32 && sec.All(c => "0123456789abcdefABCDEF".Contains(c)))
                    settings.Secret = sec.ToLower();
                else ConsoleMenu.WriteError("Secret должен быть 32 hex-символа");
                break;
            case "3":
                settings.FakeTlsDomain = ConsoleMenu.Prompt("  Домен для Fake TLS (пусто = выключить)", settings.FakeTlsDomain);
                break;
            case "4":
                settings.CfProxyEnabled = !settings.CfProxyEnabled;
                ConsoleMenu.WriteOk($"CF Proxy: {(settings.CfProxyEnabled ? "вкл" : "выкл")}");
                break;
            case "5":
                var psStr = ConsoleMenu.Prompt("  Pool size (1-16)", settings.PoolSize.ToString());
                if (int.TryParse(psStr, out var ps) && ps >= 1 && ps <= 16)
                    settings.PoolSize = ps;
                break;
            case "6":
                settings.Verbose = !settings.Verbose;
                ConsoleMenu.WriteOk($"Verbose: {(settings.Verbose ? "вкл" : "выкл")}");
                break;
            case "7":
                settings.Secret = Guid.NewGuid().ToString("N")[..32];
                ConsoleMenu.WriteOk($"Новый secret: {settings.Secret}");
                break;
        }
    }
}
