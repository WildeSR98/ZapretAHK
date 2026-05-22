using System.Diagnostics;
using System.Net.Http;
using ZapretManager.Core;
using ZapretManager.UI;

namespace ZapretManager.Diagnostics;

/// <summary>
/// Speed test using Cloudflare's CDN endpoints.
/// Measures download speed, upload speed, and latency.
/// </summary>
public static class SpeedTester
{
    public record SpeedResult(double DownloadMbps, double UploadMbps, double LatencyMs);

    // Cloudflare speed test endpoints
    private const string DownloadUrl = "https://speed.cloudflare.com/__down?bytes=10000000"; // 10MB
    private const string UploadUrl = "https://speed.cloudflare.com/__up";
    private const string LatencyUrl = "https://speed.cloudflare.com/__down?bytes=0";

    public static async Task<SpeedResult> RunAsync(Action<string>? onProgress = null)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        // ── Latency ──
        onProgress?.Invoke("Измерение задержки...");
        var latencies = new List<double>();
        for (int i = 0; i < 5; i++)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                await http.GetAsync(LatencyUrl);
                sw.Stop();
                latencies.Add(sw.Elapsed.TotalMilliseconds);
            }
            catch (Exception ex) { Logger.Error($"[SpeedTester] {ex.GetType().Name}: {ex.Message}"); }
        }
        var latency = latencies.Count > 0
            ? latencies.OrderBy(x => x).Skip(1).Take(3).Average()
            : -1;

        // ── Download ──
        onProgress?.Invoke("Тест скорости загрузки...");
        double downloadMbps = 0;
        try
        {
            var sw = Stopwatch.StartNew();
            var data = await http.GetByteArrayAsync(DownloadUrl);
            sw.Stop();

            var seconds = sw.Elapsed.TotalSeconds;
            if (seconds > 0)
                downloadMbps = Math.Round((data.Length * 8.0) / (seconds * 1_000_000), 2);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Download speed test failed: {ex.Message}");
        }

        // ── Upload ──
        onProgress?.Invoke("Тест скорости отдачи...");
        double uploadMbps = 0;
        try
        {
            var uploadData = new byte[1_000_000]; // 1MB
            new Random().NextBytes(uploadData);

            var sw = Stopwatch.StartNew();
            var content = new ByteArrayContent(uploadData);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            await http.PostAsync(UploadUrl, content);
            sw.Stop();

            var seconds = sw.Elapsed.TotalSeconds;
            if (seconds > 0)
                uploadMbps = Math.Round((uploadData.Length * 8.0) / (seconds * 1_000_000), 2);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Upload speed test failed: {ex.Message}");
        }

        return new SpeedResult(downloadMbps, uploadMbps, Math.Round(latency, 1));
    }

    public static void PrintResult(SpeedResult result, string label)
    {
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine($"\n   ── {label} ──");
        Console.ResetColor();

        Console.Write("   Download: ");
        Console.ForegroundColor = result.DownloadMbps >= 50 ? ConsoleColor.Green
            : result.DownloadMbps >= 10 ? ConsoleColor.Yellow : ConsoleColor.Red;
        Console.WriteLine($"{result.DownloadMbps:F1} Mbps");
        Console.ResetColor();

        Console.Write("   Upload:   ");
        Console.ForegroundColor = result.UploadMbps >= 20 ? ConsoleColor.Green
            : result.UploadMbps >= 5 ? ConsoleColor.Yellow : ConsoleColor.Red;
        Console.WriteLine($"{result.UploadMbps:F1} Mbps");
        Console.ResetColor();

        Console.Write("   Latency:  ");
        Console.ForegroundColor = result.LatencyMs <= 30 ? ConsoleColor.Green
            : result.LatencyMs <= 100 ? ConsoleColor.Yellow : ConsoleColor.Red;
        Console.WriteLine($"{result.LatencyMs:F0} ms");
        Console.ResetColor();
    }

    public static void PrintComparison(SpeedResult before, SpeedResult after)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("   ── СРАВНЕНИЕ ──");
        Console.ResetColor();

        PrintDelta("Download", before.DownloadMbps, after.DownloadMbps, "Mbps");
        PrintDelta("Upload  ", before.UploadMbps, after.UploadMbps, "Mbps");
        PrintDelta("Latency ", before.LatencyMs, after.LatencyMs, "ms", invert: true);
    }

    private static void PrintDelta(string label, double before, double after, string unit, bool invert = false)
    {
        if (before <= 0 || after <= 0)
        {
            Console.WriteLine($"   {label}: нет данных");
            return;
        }

        var pct = ((after - before) / before) * 100;
        var isGood = invert ? pct < 0 : pct > 0;
        var sign = pct > 0 ? "+" : "";

        Console.Write($"   {label}: {before:F1} → {after:F1} {unit}  ");
        Console.ForegroundColor = isGood ? ConsoleColor.Green : Math.Abs(pct) < 5 ? ConsoleColor.Yellow : ConsoleColor.Red;
        Console.WriteLine($"({sign}{pct:F0}%)");
        Console.ResetColor();
    }
}
