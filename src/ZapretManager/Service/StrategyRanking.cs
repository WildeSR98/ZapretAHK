using ZapretManager.Core;
using ZapretManager.UI;

namespace ZapretManager.Service;

/// <summary>
/// Loads and ranks strategies by test results.
/// </summary>
public static class StrategyRanking
{
    public record RankedStrategy(string Name, string FullPath, int Score);

    /// <summary>Load ranked strategies from last test results file.</summary>
    public static List<RankedStrategy> Load(string rootDir)
    {
        var resultsDir = Path.Combine(rootDir, "utils", "test results");
        if (!Directory.Exists(resultsDir)) return new();

        // Find latest results file
        var files = new DirectoryInfo(resultsDir)
            .GetFiles("test_results_*.txt")
            .OrderByDescending(f => f.LastWriteTime)
            .ToList();

        if (files.Count == 0) return new();

        var rankings = new List<RankedStrategy>();
        var strategiesDir = Path.Combine(rootDir, "strategies");

        foreach (var file in files.Take(1)) // Use latest only
        {
            try
            {
                var lines = File.ReadAllLines(file.FullName);
                string? currentConfig = null;
                int ok = 0, fail = 0;

                foreach (var line in lines)
                {
                    if (line.StartsWith("Config: "))
                    {
                        // Save previous
                        if (currentConfig != null)
                        {
                            var fullPath = Path.Combine(strategiesDir, currentConfig);
                            if (File.Exists(fullPath))
                                rankings.Add(new RankedStrategy(currentConfig, fullPath, ok));
                        }

                        currentConfig = line["Config: ".Length..].Split(" (")[0].Trim();
                        ok = 0; fail = 0;
                    }
                    else if (line.Contains("HTTP:OK") || line.Contains("TLS1.2:OK") || line.Contains("TLS1.3:OK"))
                    {
                        ok++;
                    }
                    else if (line.Contains("HTTP:") || line.Contains("TLS1."))
                    {
                        fail++;
                    }
                }

                // Save last
                if (currentConfig != null)
                {
                    var fullPath = Path.Combine(strategiesDir, currentConfig);
                    if (File.Exists(fullPath))
                        rankings.Add(new RankedStrategy(currentConfig, fullPath, ok));
                }
            }
            catch (Exception ex) { Logger.Warn($"StrategyRanking parse error: {ex.Message}"); }
        }

        return rankings.OrderByDescending(r => r.Score).ToList();
    }

    /// <summary>Get next strategy after current one in ranking.</summary>
    public static RankedStrategy? GetNext(List<RankedStrategy> ranking, string? currentStrategy)
    {
        if (ranking.Count == 0) return null;
        if (string.IsNullOrEmpty(currentStrategy)) return ranking[0];

        var idx = ranking.FindIndex(r =>
            r.Name.Equals(currentStrategy + ".bat", StringComparison.OrdinalIgnoreCase) ||
            r.Name.Equals(currentStrategy, StringComparison.OrdinalIgnoreCase));

        if (idx < 0) return ranking[0];
        if (idx + 1 < ranking.Count) return ranking[idx + 1];
        return ranking[0]; // Wrap around
    }
}
