using ZapretManager.Core;
using ZapretManager.UI;
using ZapretManager.Service;
using ZapretManager.Diagnostics;
using Spectre.Console;

namespace ZapretManager.Menus;

/// <summary>
/// Service-related menu actions: Install, Remove, Status (пункты 1-3).
/// Extracted gradually from Program.cs as part of the ongoing refactor.
/// </summary>
internal static class ServiceMenu
{
    // ── Install service (п.1) ────────────────────────────────────────────────

    /// <summary>
    /// Interactive strategy picker + service installation.
    /// Uses Spectre.Console SelectionPrompt when running in an interactive terminal.
    /// </summary>
    internal static async Task InstallAsync(string rootDir, string binDir, string listsDir, string utilsDir)
    {
        Console.Clear();
        ConsoleMenu.WriteHeader("УСТАНОВКА СЛУЖБЫ");

        var files = StrategyReader.GetStrategyFiles(rootDir);
        if (files.Length == 0)
        {
            ConsoleMenu.WriteError("Стратегии не найдены в папке strategies/");
            ConsoleMenu.PauseAny();
            return;
        }

        // ── Strategy picker via Spectre SelectionPrompt (3.3) ───────────────────
        var bat = ConsoleMenu.PickFromList(
            "Выберите стратегию",
            files,
            display: f => f.Name);

        // ── Install ──────────────────────────────────────────────────────────
        var gf      = GameFilter.Get(utilsDir);
        var winws   = Path.Combine(binDir, "winws.exe");
        var batArgs = StrategyReader.ParseArgs(bat.FullName, binDir, listsDir, gf.Tcp, gf.Udp);

        ConsoleMenu.WriteStep($"Устанавливаю службу: {bat.Name}");

        RunNetsh("interface tcp set global timestamps=enabled");

        var binPath = $"\"{winws}\" {batArgs}";
        var ok = WinServiceManager.Install("zapret", "zapret", "Zapret DPI bypass", binPath);

        // Save strategy name to registry
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(
                @"System\CurrentControlSet\Services\zapret");
            key?.SetValue("zapret-discord-youtube",
                Path.GetFileNameWithoutExtension(bat.Name));
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to save strategy name to registry: {ex.Message}");
        }

        if (ok)
        {
            ConsoleMenu.WriteOk($"Служба zapret установлена и запущена: {bat.Name}");
        }
        else
        {
            var state = WinServiceManager.GetState("zapret");
            if (state == WinServiceManager.ServiceState.NotInstalled)
            {
                ConsoleMenu.WriteError("Не удалось создать службу. Проверьте права администратора.");
            }
            else
            {
                ConsoleMenu.WriteError("Служба создана, но НЕ запустилась (winws.exe упал)");
                if (!File.Exists(Path.Combine(binDir, "WinDivert64.sys")))
                    ConsoleMenu.WriteError("  ✗ WinDivert64.sys НЕ найден");
                if (!File.Exists(Path.Combine(binDir, "WinDivert.dll")))
                    ConsoleMenu.WriteError("  ✗ WinDivert.dll НЕ найден");
                if (!File.Exists(Path.Combine(binDir, "cygwin1.dll")))
                    ConsoleMenu.WriteError("  ✗ cygwin1.dll НЕ найден");
                ConsoleMenu.WriteInfo("Добавьте папку bin/ в исключения антивируса и попробуйте снова");
            }
        }

        ConsoleMenu.PauseAny();
        await Task.CompletedTask;
    }

    // ── Remove services (п.2) ────────────────────────────────────────────────

    internal static async Task RemoveAsync()
    {
        Console.Clear();
        ConsoleMenu.WriteHeader("УДАЛЕНИЕ СЛУЖБ");
        ProcessManager.KillAll();
        foreach (var svc in new[] { "zapret", "WinDivert", "WinDivert14" })
        {
            WinServiceManager.Stop(svc);
            if (WinServiceManager.Remove(svc))
                ConsoleMenu.WriteOk($"Служба удалена: {svc}");
        }
        ConsoleMenu.PauseAny();
        await Task.CompletedTask;
    }

    // ── Service status (п.3) ─────────────────────────────────────────────────

    internal static void Status(string binDir, string utilsDir, string rootDir)
    {
        Console.Clear();
        ConsoleMenu.WriteHeader("СТАТУС СЛУЖБ");

        // ── Status table via Spectre ─────────────────────────────────────────
        var rows = new List<(string, string)>();

        foreach (var svc in new[] { "zapret", "WinDivert" })
        {
            var st = WinServiceManager.GetState(svc);
            var (label, colour) = st switch
            {
                WinServiceManager.ServiceState.Running     => ("запущена",           "green"),
                WinServiceManager.ServiceState.Stopped     => ("остановлена",        "yellow"),
                WinServiceManager.ServiceState.Starting    => ("запускается",        "cyan"),
                WinServiceManager.ServiceState.Stopping    => ("останавливается",    "cyan"),
                WinServiceManager.ServiceState.NotInstalled=> ("не установлена",     "dim"),
                _                                          => ("неизвестно",         "grey"),
            };
            rows.Add(($"Служба [{svc}]", $"[{colour}]{label}[/]"));

            if (svc == "zapret" && st == WinServiceManager.ServiceState.Stopped)
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo(
                        "cmd", $"/c sc query \"{svc}\" | findstr WIN32_EXIT_CODE")
                    {
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    var proc   = System.Diagnostics.Process.Start(psi);
                    var output = proc?.StandardOutput.ReadToEnd()?.Trim() ?? "";
                    proc?.WaitForExit(3000);
                    if (!string.IsNullOrEmpty(output))
                        rows.Add(("  exit code", $"[dim]{Markup.Escape(output)}[/]"));
                    if (output.Contains("1067"))
                        rows.Add(("  совет", "[dim]Код 1067 — процесс упал. Переустановите через п.1[/]"));
                }
                catch (Exception ex) { Core.Logger.Error($"[ServiceMenu] {ex.GetType().Name}: {ex.Message}"); }
            }
        }

        var wd14 = WinServiceManager.GetState("WinDivert14");
        if (wd14 != WinServiceManager.ServiceState.NotInstalled)
            rows.Add(("Служба [WinDivert14]", "[yellow]устаревшая, удалите через п.2[/]"));

        // Files
        rows.Add(("WinDivert64.sys",
            File.Exists(Path.Combine(binDir, "WinDivert64.sys")) ? "[green]найден[/]" : "[red]НЕ НАЙДЕН[/]"));
        rows.Add(("winws.exe процесс",
            ProcessManager.IsRunning("winws") ? "[green]запущен[/]" : "[yellow]не запущен[/]"));

        // Strategy from registry
        string strategy = "?";
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"System\CurrentControlSet\Services\zapret");
            strategy = key?.GetValue("zapret-discord-youtube")?.ToString() ?? "?";
            var imagePath = key?.GetValue("ImagePath")?.ToString() ?? "";
            if (imagePath.StartsWith("\""))
            {
                var end = imagePath.IndexOf('"', 1);
                if (end > 0) imagePath = imagePath[1..end];
            }
            if (!string.IsNullOrEmpty(imagePath))
                rows.Add(("Путь winws", $"[dim]{Markup.Escape(imagePath)}[/]"));
        }
        catch (Exception ex) { Core.Logger.Error($"[ServiceMenu] {ex.GetType().Name}: {ex.Message}"); }

        rows.Add(("Стратегия", $"[cyan]{Markup.Escape(strategy)}[/]"));

        ConsoleMenu.WriteStatusTable(rows);
        AnsiConsole.WriteLine();
        ConsoleMenu.PauseAny();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void RunNetsh(string args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("netsh", args)
            {
                CreateNoWindow = true, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            p?.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            Logger.Warn($"netsh failed: {ex.Message}");
        }
    }
}

// ── Internal logger alias ────────────────────────────────────────────────────
file static class Logger
{
    public static void Warn(string m) => Core.Logger.Warn(m);
}
