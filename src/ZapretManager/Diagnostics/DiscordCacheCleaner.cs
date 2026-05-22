using System.Diagnostics;

namespace ZapretManager.Diagnostics;


/// <summary>
/// Discord cache cleaner — mirrors the functionality from original service.bat (lines 667-700).
/// </summary>
public static class DiscordCacheCleaner
{
    private static readonly string[] CacheFolders = { "Cache", "Code Cache", "GPUCache" };

    /// <summary>
    /// Closes Discord if running and clears cache directories.
    /// Returns whether Discord was closed, list of deleted paths, and list of failed paths.
    /// </summary>
    public static async Task<(bool Closed, List<string> Deleted, List<string> Failed)> Clean()
    {
        bool closed = false;
        var deleted = new List<string>();
        var failed = new List<string>();

        // Kill Discord if running
        var discordProcs = Process.GetProcessesByName("Discord");
        if (discordProcs.Length > 0)
        {
            foreach (var p in discordProcs)
            {
                try { p.Kill(); } catch { }
            }
            await Task.Delay(1000); // Wait for process to fully exit
            closed = true;
        }

        // Delete cache directories
        var discordCacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "discord");

        foreach (var folder in CacheFolders)
        {
            var dirPath = Path.Combine(discordCacheDir, folder);
            if (Directory.Exists(dirPath))
            {
                try
                {
                    Directory.Delete(dirPath, recursive: true);
                    deleted.Add(dirPath);
                }
                catch
                {
                    failed.Add(dirPath);
                }
            }
        }

        return (closed, deleted, failed);
    }
}
