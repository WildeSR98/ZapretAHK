using System.Diagnostics;
using ZapretManager.Core;
using ZapretManager.UI;

namespace ZapretManager.Diagnostics;

/// <summary>
/// Real-time network traffic monitoring via netstat.
/// </summary>
public static class TrafficMonitor
{
    public record TrafficSnapshot(
        long BytesReceived, long BytesSent,
        long PacketsReceived, long PacketsSent,
        DateTime Timestamp);

    /// <summary>Get current network statistics via netstat -e.</summary>
    public static TrafficSnapshot? GetSnapshot()
    {
        try
        {
            var psi = new ProcessStartInfo("netstat", "-e")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return null;

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);

            return ParseNetstat(output);
        }
        catch { return null; }
    }

    /// <summary>Run live monitoring for durationSeconds, updating every second.</summary>
    public static async Task RunLiveMonitorAsync(int durationSeconds = 30)
    {
        ConsoleMenu.WriteStep($"Мониторинг трафика ({durationSeconds} сек). Нажмите ESC для выхода.");
        Console.WriteLine();

        var first = GetSnapshot();
        if (first == null)
        {
            ConsoleMenu.WriteError("Не удалось получить статистику сети");
            return;
        }

        var prev = first;
        Console.CursorVisible = false;

        for (int i = 0; i < durationSeconds; i++)
        {
            if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape)
                break;

            await Task.Delay(1000);
            var current = GetSnapshot();
            if (current == null) continue;

            var rxSpeed = (current.BytesReceived - prev.BytesReceived);
            var txSpeed = (current.BytesSent - prev.BytesSent);
            var rxPkt = current.PacketsReceived - prev.PacketsReceived;
            var txPkt = current.PacketsSent - prev.PacketsSent;

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"\r   ↓ {FormatBytes(rxSpeed)}/с ({rxPkt} пкт)");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"  ↑ {FormatBytes(txSpeed)}/с ({txPkt} пкт)");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"  [{i + 1}/{durationSeconds}с]    ");
            Console.ResetColor();

            prev = current;
        }

        Console.CursorVisible = true;
        Console.WriteLine();

        // Summary
        var last = GetSnapshot();
        if (last != null && first != null)
        {
            Console.WriteLine();
            var totalRx = last.BytesReceived - first.BytesReceived;
            var totalTx = last.BytesSent - first.BytesSent;
            ConsoleMenu.WriteInfo($"Итого за сессию: ↓ {FormatBytes(totalRx)} / ↑ {FormatBytes(totalTx)}");
        }
    }

    /// <summary>Show zapret-related process info.</summary>
    public static void ShowZapretProcesses()
    {
        try
        {
            var winws = Process.GetProcessesByName("winws");
            if (winws.Length == 0)
            {
                ConsoleMenu.WriteInfo("winws.exe не запущен");
                return;
            }

            foreach (var p in winws)
            {
                try
                {
                    var mem = p.WorkingSet64 / 1024 / 1024;
                    var cpu = p.TotalProcessorTime;
                    ConsoleMenu.WriteOk($"winws.exe PID={p.Id}  RAM={mem} MB  CPU={cpu.TotalSeconds:F1}с");
                }
                catch { ConsoleMenu.WriteInfo($"winws.exe PID={p.Id}"); }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"ShowZapretProcesses: {ex.Message}");
        }
    }

    private static TrafficSnapshot? ParseNetstat(string output)
    {
        try
        {
            long bytesRx = 0, bytesTx = 0, pktsRx = 0, pktsTx = 0;
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3) continue;

                // Parse "Bytes           123456        789012" format
                if (trimmed.StartsWith("Bytes", StringComparison.OrdinalIgnoreCase)
                    || trimmed.StartsWith("Байт", StringComparison.OrdinalIgnoreCase))
                {
                    if (long.TryParse(parts[1], out var rx)) bytesRx = rx;
                    if (long.TryParse(parts[2], out var tx)) bytesTx = tx;
                }
                else if (trimmed.StartsWith("Unicast", StringComparison.OrdinalIgnoreCase)
                    || trimmed.StartsWith("Одноадрес", StringComparison.OrdinalIgnoreCase))
                {
                    if (long.TryParse(parts[parts.Length - 2], out var rx)) pktsRx = rx;
                    if (long.TryParse(parts[parts.Length - 1], out var tx)) pktsTx = tx;
                }
            }

            return new TrafficSnapshot(bytesRx, bytesTx, pktsRx, pktsTx, DateTime.Now);
        }
        catch { return null; }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / 1024.0 / 1024.0:F1} MB";
    }
}
