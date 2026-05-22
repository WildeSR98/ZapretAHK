using ZapretManager.UI;
using ZapretManager.Diagnostics;

namespace ZapretManager.Menus;

/// <summary>
/// Traffic monitor menu action (пункт 16).
/// Extracted from Program.cs as part of the ongoing refactor.
/// Note: SpeedTest (п.18) and Watchdog (п.17) stay in Program.cs due to
/// dependency on StopZapretForTest() / _watchdog instance.
/// </summary>
internal static class MonitorMenu
{
    // ── Traffic monitor (п.16) ───────────────────────────────────────────────

    internal static async Task TrafficMonitorAsync()
    {
        Console.Clear();
        ConsoleMenu.WriteHeader("МОНИТОРИНГ ТРАФИКА");

        // Show zapret processes
        ConsoleMenu.WriteStep("Процессы zapret");
        TrafficMonitor.ShowZapretProcesses();
        Console.WriteLine();

        // Live monitor (60 seconds)
        await TrafficMonitor.RunLiveMonitorAsync(60);

        ConsoleMenu.PauseAny();
    }
}
