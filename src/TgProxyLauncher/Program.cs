using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TgProxyLauncher;

class Program
{
    static string RootDir = "";
    static string ProxyDir = "";
    static string PythonExe = "";
    static string ProxyScript = "";
    static string SettingsFile = "";
    static ProxySettings Settings = new();
    static Process? ProxyProcess;

    static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.Title = "TG WS Proxy Launcher";

        RootDir = DetectRootDir();
        ProxyDir = Path.Combine(RootDir, "tg-proxy");
        PythonExe = Path.Combine(ProxyDir, "python", "python.exe");
        ProxyScript = Path.Combine(ProxyDir, "proxy", "tg_ws_proxy.py");
        SettingsFile = Path.Combine(RootDir, "tg-proxy-settings.json");

        if (!File.Exists(PythonExe))
        {
            WriteError("Embedded Python не найден!");
            WriteInfo($"Ожидается: {PythonExe}");
            WriteInfo("Скачайте версию с TG Proxy (zapret-manager-tgproxy.zip)");
            PauseAny();
            return;
        }
        if (!File.Exists(ProxyScript))
        {
            WriteError("Скрипт прокси не найден!");
            WriteInfo($"Ожидается: {ProxyScript}");
            PauseAny();
            return;
        }

        LoadSettings();

        // Direct launch mode
        if (args.Contains("--start"))
        {
            await StartProxy(interactive: false);
            return;
        }

        // Menu mode
        while (true)
        {
            Console.Clear();
            PrintHeader();
            Console.Write("   Выберите (1-5, 0=выход): ");
            var choice = Console.ReadLine()?.Trim();

            switch (choice)
            {
                case "1": await StartProxy(interactive: true); break;
                case "2": StopProxy(); PauseAny(); break;
                case "3": ShowLink(); PauseAny(); break;
                case "4": EditSettings(); break;
                case "5": ShowStatus(); PauseAny(); break;
                case "0": StopProxy(); return;
            }
        }
    }

    // ── UI ─────────────────────────────────────────────────────────────────────

    static void PrintHeader()
    {
        var running = IsRunning();
        var status = running ? "запущен" : "остановлен";
        var statusColor = running ? ConsoleColor.Green : ConsoleColor.DarkGray;

        var ver = "?";
        var vf = Path.Combine(ProxyDir, "version.txt");
        if (File.Exists(vf)) ver = File.ReadAllText(vf).Trim();

        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("\n  ╔══════════════════════════════════════════════════════════╗");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine( "  ║  TG WS PROXY LAUNCHER                                   ║");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write(     "  ║  Статус: ");
        Console.ForegroundColor = statusColor;
        Console.Write($"{status,-14}");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  Proxy v{ver,-14}    ║");
        Console.WriteLine($"  ║  Порт: {Settings.Port,-17}  Secret: {Settings.Secret[..Math.Min(8, Settings.Secret.Length)]}...       ║");
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine( "  ╚══════════════════════════════════════════════════════════╝");
        Console.ResetColor();

        Console.WriteLine("\n  :: УПРАВЛЕНИЕ");
        Console.WriteLine("     1. Запустить прокси");
        Console.WriteLine("     2. Остановить прокси");
        Console.WriteLine("     3. Показать ссылку для Telegram");
        Console.WriteLine("     4. Настройки");
        Console.WriteLine("     5. Подробный статус");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("\n  ─────────────────────────────────────────────────────────");
        Console.WriteLine("     0. Выход (прокси остановится)");
        Console.ResetColor();
        Console.WriteLine();
    }

    // ── Proxy Control ──────────────────────────────────────────────────────────

    static async Task StartProxy(bool interactive)
    {
        if (IsRunning())
        {
            WriteWarn("Прокси уже запущен");
            if (interactive) PauseAny();
            return;
        }

        WriteStep("Запуск TG WS Proxy...");

        var argsList = new List<string>
        {
            $"--host {Settings.Host}",
            $"--port {Settings.Port}",
            $"--secret {Settings.Secret}",
            $"--buf-kb {Settings.BufKb}",
            $"--pool-size {Settings.PoolSize}",
        };

        if (!string.IsNullOrEmpty(Settings.FakeTlsDomain))
            argsList.Add($"--fake-tls-domain {Settings.FakeTlsDomain}");

        if (!Settings.CfProxyEnabled)
            argsList.Add("--no-cfproxy");

        if (!string.IsNullOrEmpty(Settings.CfProxyDomain))
            argsList.Add($"--cfproxy-domain {Settings.CfProxyDomain}");

        foreach (var dcIp in Settings.DcIps)
            argsList.Add($"--dc-ip {dcIp}");

        if (Settings.Verbose)
            argsList.Add("-v");

        var logFile = Path.Combine(RootDir, "logs", "tg-proxy.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
        argsList.Add($"--log-file \"{logFile}\"");
        argsList.Add($"--log-max-mb {Settings.LogMaxMb}");

        var allArgs = $"\"{ProxyScript}\" {string.Join(" ", argsList)}";

        ProxyProcess = Process.Start(new ProcessStartInfo(PythonExe, allArgs)
        {
            WorkingDirectory = ProxyDir,
            UseShellExecute = false,
            CreateNoWindow = false,
        });

        await Task.Delay(1500);

        if (ProxyProcess != null && !ProxyProcess.HasExited)
        {
            WriteOk($"Прокси запущен (PID: {ProxyProcess.Id})");
            Console.WriteLine();
            ShowLink();
        }
        else
        {
            WriteError("Не удалось запустить прокси");
        }

        if (interactive) PauseAny();
    }

    static void StopProxy()
    {
        // Kill our tracked process
        if (ProxyProcess != null && !ProxyProcess.HasExited)
        {
            try { ProxyProcess.Kill(true); } catch { }
            ProxyProcess = null;
            WriteOk("Прокси остановлен");
            return;
        }

        // Kill any python running tg_ws_proxy
        var killed = false;
        foreach (var p in Process.GetProcessesByName("python"))
        {
            try
            {
                var cmdLine = GetCommandLine(p);
                if (cmdLine.Contains("tg_ws_proxy"))
                {
                    p.Kill(true);
                    killed = true;
                }
            }
            catch { }
        }

        if (killed) WriteOk("Прокси остановлен");
        else WriteInfo("Прокси не был запущен");
    }

    static void ShowLink()
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  ═══════════ ССЫЛКА ДЛЯ TELEGRAM ═══════════");
        Console.ResetColor();

        string link;
        if (!string.IsNullOrEmpty(Settings.FakeTlsDomain))
        {
            var domainHex = BitConverter.ToString(
                Encoding.ASCII.GetBytes(Settings.FakeTlsDomain))
                .Replace("-", "").ToLower();
            link = $"tg://proxy?server={Settings.Host}&port={Settings.Port}" +
                   $"&secret=ee{Settings.Secret}{domainHex}";
        }
        else
        {
            link = $"tg://proxy?server={Settings.Host}&port={Settings.Port}" +
                   $"&secret=dd{Settings.Secret}";
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\n  {link}");
        Console.ResetColor();

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("\n  Скопируйте ссылку и вставьте в Telegram.");
        Console.WriteLine("  Или откройте в браузере — Telegram подхватит автоматически.");
        Console.ResetColor();
        Console.WriteLine();
    }

    static void ShowStatus()
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n  ═══════════ СТАТУС ═══════════");
        Console.ResetColor();

        var running = IsRunning();
        Console.Write("  Прокси: ");
        Console.ForegroundColor = running ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine(running ? "ЗАПУЩЕН" : "ОСТАНОВЛЕН");
        Console.ResetColor();

        Console.WriteLine($"  Хост:         {Settings.Host}");
        Console.WriteLine($"  Порт:         {Settings.Port}");
        Console.WriteLine($"  Secret:       {Settings.Secret}");
        Console.WriteLine($"  Fake TLS:     {(string.IsNullOrEmpty(Settings.FakeTlsDomain) ? "выключен" : Settings.FakeTlsDomain)}");
        Console.WriteLine($"  CF Proxy:     {(Settings.CfProxyEnabled ? "включён" : "выключен")}");
        Console.WriteLine($"  Pool size:    {Settings.PoolSize}");
        Console.WriteLine($"  Buffer:       {Settings.BufKb} KB");
        Console.WriteLine($"  DC IPs:       {string.Join(", ", Settings.DcIps)}");
        Console.WriteLine($"  Verbose:      {(Settings.Verbose ? "да" : "нет")}");

        var logFile = Path.Combine(RootDir, "logs", "tg-proxy.log");
        if (File.Exists(logFile))
        {
            var fi = new FileInfo(logFile);
            Console.WriteLine($"  Лог:          {logFile} ({fi.Length / 1024} KB)");
        }
        Console.WriteLine();
    }

    // ── Settings ───────────────────────────────────────────────────────────────

    static void EditSettings()
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n  ═══════════ НАСТРОЙКИ ═══════════");
        Console.ResetColor();

        Console.WriteLine($"\n  Текущие значения:");
        Console.WriteLine($"    [1] Порт:         {Settings.Port}");
        Console.WriteLine($"    [2] Secret:       {Settings.Secret}");
        Console.WriteLine($"    [3] Fake TLS:     {(string.IsNullOrEmpty(Settings.FakeTlsDomain) ? "выключен" : Settings.FakeTlsDomain)}");
        Console.WriteLine($"    [4] CF Proxy:     {(Settings.CfProxyEnabled ? "вкл" : "выкл")}");
        Console.WriteLine($"    [5] Pool size:    {Settings.PoolSize}");
        Console.WriteLine($"    [6] Verbose:      {(Settings.Verbose ? "вкл" : "выкл")}");
        Console.WriteLine($"    [7] Новый secret  (сгенерировать)");
        Console.WriteLine($"    [0] Назад");

        Console.Write("\n  Выберите параметр: ");
        var ch = Console.ReadLine()?.Trim();

        switch (ch)
        {
            case "1":
                Console.Write("  Новый порт: ");
                if (int.TryParse(Console.ReadLine()?.Trim(), out var port) && port > 0 && port < 65536)
                    Settings.Port = port;
                break;
            case "2":
                Console.Write("  Новый secret (32 hex): ");
                var sec = Console.ReadLine()?.Trim() ?? "";
                if (sec.Length == 32 && sec.All(c => "0123456789abcdefABCDEF".Contains(c)))
                    Settings.Secret = sec.ToLower();
                else WriteError("Secret должен быть 32 hex-символа");
                break;
            case "3":
                Console.Write("  Домен для Fake TLS (пусто = выключить): ");
                Settings.FakeTlsDomain = Console.ReadLine()?.Trim() ?? "";
                break;
            case "4":
                Settings.CfProxyEnabled = !Settings.CfProxyEnabled;
                WriteOk($"CF Proxy: {(Settings.CfProxyEnabled ? "вкл" : "выкл")}");
                break;
            case "5":
                Console.Write("  Pool size (1-16): ");
                if (int.TryParse(Console.ReadLine()?.Trim(), out var ps) && ps >= 1 && ps <= 16)
                    Settings.PoolSize = ps;
                break;
            case "6":
                Settings.Verbose = !Settings.Verbose;
                WriteOk($"Verbose: {(Settings.Verbose ? "вкл" : "выкл")}");
                break;
            case "7":
                Settings.Secret = Guid.NewGuid().ToString("N")[..32];
                WriteOk($"Новый secret: {Settings.Secret}");
                break;
            case "0": return;
        }

        SaveSettings();
        WriteOk("Настройки сохранены");
        if (ch != "0") { Thread.Sleep(800); }
    }

    static void LoadSettings()
    {
        if (File.Exists(SettingsFile))
        {
            try
            {
                var json = File.ReadAllText(SettingsFile);
                Settings = JsonSerializer.Deserialize<ProxySettings>(json) ?? new();
            }
            catch { Settings = new(); }
        }

        // Generate secret if empty
        if (string.IsNullOrEmpty(Settings.Secret))
        {
            Settings.Secret = Guid.NewGuid().ToString("N")[..32];
            SaveSettings();
        }
    }

    static void SaveSettings()
    {
        var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        File.WriteAllText(SettingsFile, json, Encoding.UTF8);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    static bool IsRunning()
    {
        if (ProxyProcess != null && !ProxyProcess.HasExited) return true;

        foreach (var p in Process.GetProcessesByName("python"))
        {
            try
            {
                var cmd = GetCommandLine(p);
                if (cmd.Contains("tg_ws_proxy")) return true;
            }
            catch { }
        }
        return false;
    }

    static string GetCommandLine(Process p)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {p.Id}");
            foreach (var obj in searcher.Get())
                return obj["CommandLine"]?.ToString() ?? "";
        }
        catch { }
        return "";
    }

    static string DetectRootDir()
    {
        var dir = AppContext.BaseDirectory.TrimEnd('\\', '/');
        var candidate = dir;
        for (int i = 0; i < 5; i++)
        {
            if (Directory.Exists(Path.Combine(candidate, "tg-proxy", "proxy")))
                return candidate;
            var parent = Path.GetDirectoryName(candidate);
            if (parent == null || parent == candidate) break;
            candidate = parent;
        }
        return dir;
    }

    // ── Console Helpers ────────────────────────────────────────────────────────

    static void WriteOk(string msg)    { Write("[OK] ",    ConsoleColor.Green,  msg); }
    static void WriteError(string msg) { Write("[ОШИБКА] ",ConsoleColor.Red,    msg); }
    static void WriteWarn(string msg)  { Write("[!] ",     ConsoleColor.Yellow,  msg); }
    static void WriteInfo(string msg)  { Write("[i] ",     ConsoleColor.DarkCyan,msg); }
    static void WriteStep(string msg)  { Write("[>] ",     ConsoleColor.Cyan,    msg); }

    static void Write(string prefix, ConsoleColor color, string msg)
    {
        Console.ForegroundColor = color;
        Console.Write($"  {prefix}");
        Console.ResetColor();
        Console.WriteLine(msg);
    }

    static void PauseAny()
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("\n  Нажмите любую клавишу...");
        Console.ResetColor();
        Console.ReadKey(true);
    }
}

class ProxySettings
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 1443;
    public string Secret { get; set; } = "";
    public string FakeTlsDomain { get; set; } = "";
    public bool CfProxyEnabled { get; set; } = true;
    public string CfProxyDomain { get; set; } = "";
    public int PoolSize { get; set; } = 4;
    public int BufKb { get; set; } = 256;
    public int LogMaxMb { get; set; } = 5;
    public bool Verbose { get; set; } = false;
    public List<string> DcIps { get; set; } = new() { "2:149.154.167.220", "4:149.154.167.220" };
}
