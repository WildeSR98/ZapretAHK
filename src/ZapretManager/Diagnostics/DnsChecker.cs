using System.Net;
using ZapretManager.Core;
using ZapretManager.UI;

namespace ZapretManager.Diagnostics;

/// <summary>
/// DNS resolution checker — verifies that domain names resolve correctly.
/// </summary>
public static class DnsChecker
{
    public record DnsResult(string Host, bool Resolved, string[] IPs, long TimeMs, string? Error);

    /// <summary>Check DNS resolution for all configured targets.</summary>
    public static async Task<List<DnsResult>> CheckAllAsync(IList<Core.CheckTarget> targets)
    {
        var tasks = targets
            .Where(t => !string.IsNullOrEmpty(t.Host))
            .Select(t => CheckOneAsync(t.Host));
        return (await Task.WhenAll(tasks)).ToList();
    }

    /// <summary>Resolve a single hostname and measure time.</summary>
    public static async Task<DnsResult> CheckOneAsync(string host)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host);
            sw.Stop();
            var ips = addresses
                .Select(a => a.ToString())
                .ToArray();

            return new DnsResult(host, ips.Length > 0, ips, sw.ElapsedMilliseconds, null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new DnsResult(host, false, Array.Empty<string>(), sw.ElapsedMilliseconds, ex.Message);
        }
    }

    /// <summary>Print DNS results to console.</summary>
    public static void PrintResults(List<DnsResult> results)
    {
        foreach (var r in results)
        {
            if (r.Resolved)
            {
                ConsoleMenu.WriteOk($"{r.Host}: {string.Join(", ", r.IPs)} ({r.TimeMs} мс)");
            }
            else
            {
                ConsoleMenu.WriteError($"{r.Host}: не резолвится — {r.Error}");
            }
        }

        var resolved = results.Count(r => r.Resolved);
        var total = results.Count;
        Console.WriteLine();

        if (resolved == total)
            ConsoleMenu.WriteOk($"DNS: {resolved}/{total} хостов резолвятся");
        else if (resolved > 0)
            ConsoleMenu.WriteWarn($"DNS: {resolved}/{total} хостов резолвятся (возможна проблема DNS)");
        else
            ConsoleMenu.WriteError($"DNS: ни один хост не резолвится. Проверьте DNS-сервер.");
    }
}
