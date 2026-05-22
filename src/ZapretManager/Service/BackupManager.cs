using System.IO.Compression;
using ZapretManager.Core;
using ZapretManager.UI;

namespace ZapretManager.Service;

/// <summary>
/// Backup and restore of zapret configuration and files.
/// </summary>
public static class BackupManager
{
    private const string BackupsDir = "backups";
    private static readonly string[] IncludeDirs = { "bin", "lists", "strategies", "utils" };
    private static readonly string[] IncludeRootFiles = { "config.json" };

    /// <summary>Create a backup ZIP of the current installation.</summary>
    public static string? CreateBackup(string rootDir, int keepCount = 5)
    {
        try
        {
            var backupsPath = Path.Combine(rootDir, BackupsDir);
            Directory.CreateDirectory(backupsPath);

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var zipName = $"zapret_backup_{timestamp}.zip";
            var zipPath = Path.Combine(backupsPath, zipName);

            ConsoleMenu.StartSpinner("Создание бэкапа...");

            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                // Add directories
                foreach (var dir in IncludeDirs)
                {
                    var dirPath = Path.Combine(rootDir, dir);
                    if (!Directory.Exists(dirPath)) continue;

                    foreach (var file in Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories))
                    {
                        // Skip large/temporary files
                        var fi = new FileInfo(file);
                        if (fi.Length > 50 * 1024 * 1024) continue; // Skip files > 50MB
                        if (fi.Name.EndsWith(".log")) continue;

                        var entryName = Path.GetRelativePath(rootDir, file).Replace('\\', '/');
                        try { zip.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal); }
                        catch (Exception ex) { Logger.Error($"[BackupManager] {ex.GetType().Name}: {ex.Message}"); /* skip locked files */ }
                    }
                }

                // Add root files
                foreach (var fileName in IncludeRootFiles)
                {
                    var filePath = Path.Combine(rootDir, fileName);
                    if (File.Exists(filePath))
                        zip.CreateEntryFromFile(filePath, fileName, CompressionLevel.Optimal);
                }
            }

            ConsoleMenu.StopSpinner(true, $"Бэкап создан: {zipName}");

            // Rotate old backups
            RotateBackups(backupsPath, keepCount);

            var size = new FileInfo(zipPath).Length / 1024;
            Logger.Ok($"Бэкап создан: {zipPath} ({size} KB)");
            return zipPath;
        }
        catch (Exception ex)
        {
            ConsoleMenu.StopSpinner(false, $"Ошибка бэкапа: {ex.Message}");
            Logger.Error($"Backup failed: {ex}");
            return null;
        }
    }

    /// <summary>Restore from a backup ZIP.</summary>
    public static bool RestoreBackup(string rootDir, string zipPath)
    {
        try
        {
            if (!File.Exists(zipPath))
            {
                ConsoleMenu.WriteError($"Файл не найден: {zipPath}");
                return false;
            }

            ConsoleMenu.StartSpinner("Восстановление из бэкапа...");

            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue; // skip directory entries

                var targetPath = Path.Combine(rootDir, entry.FullName.Replace('/', '\\'));
                var targetDir = Path.GetDirectoryName(targetPath);
                if (targetDir != null) Directory.CreateDirectory(targetDir);

                try { entry.ExtractToFile(targetPath, overwrite: true); }
                catch (Exception ex) { Logger.Error($"[BackupManager] {ex.GetType().Name}: {ex.Message}"); /* skip locked files */ }
            }

            ConsoleMenu.StopSpinner(true, "Восстановление завершено");
            Logger.Ok($"Восстановлено из: {zipPath}");
            return true;
        }
        catch (Exception ex)
        {
            ConsoleMenu.StopSpinner(false, $"Ошибка: {ex.Message}");
            Logger.Error($"Restore failed: {ex}");
            return false;
        }
    }

    /// <summary>List existing backups.</summary>
    public static FileInfo[] ListBackups(string rootDir)
    {
        var backupsPath = Path.Combine(rootDir, BackupsDir);
        if (!Directory.Exists(backupsPath)) return Array.Empty<FileInfo>();
        return new DirectoryInfo(backupsPath)
            .GetFiles("zapret_backup_*.zip")
            .OrderByDescending(f => f.CreationTime)
            .ToArray();
    }

    /// <summary>Keep only the newest N backups.</summary>
    private static void RotateBackups(string backupsPath, int keepCount)
    {
        try
        {
            var files = new DirectoryInfo(backupsPath)
                .GetFiles("zapret_backup_*.zip")
                .OrderByDescending(f => f.CreationTime)
                .Skip(keepCount);

            foreach (var f in files)
            {
                f.Delete();
                Logger.Info($"Старый бэкап удалён: {f.Name}");
            }
        }
        catch (Exception ex) { Logger.Error($"[BackupManager] {ex.GetType().Name}: {ex.Message}"); }
    }
}
