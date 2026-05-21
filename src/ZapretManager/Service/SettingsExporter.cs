using System.IO.Compression;
using System.Text.Json;
using ZapretManager.Core;
using ZapretManager.UI;

namespace ZapretManager.Service;

/// <summary>
/// Full settings export/import — ZIP archive with all configs, profiles, lists, strategies.
/// </summary>
public static class SettingsExporter
{
    public static void Run(string rootDir, string listsDir)
    {
        while (true)
        {
            Console.Clear();
            ConsoleMenu.WriteHeader("ЭКСПОРТ / ИМПОРТ НАСТРОЕК");
            Console.WriteLine();
            Console.WriteLine("   1. Экспорт настроек (ZIP)");
            Console.WriteLine("   2. Импорт настроек (ZIP)");
            Console.WriteLine("   0. Назад");
            Console.WriteLine();

            var choice = ConsoleMenu.Prompt("Выберите", "0");
            switch (choice)
            {
                case "1": Export(rootDir, listsDir); break;
                case "2": Import(rootDir, listsDir); break;
                case "0": return;
            }
        }
    }

    private static void Export(string rootDir, string listsDir)
    {
        Console.Clear();
        ConsoleMenu.WriteHeader("ЭКСПОРТ НАСТРОЕК");
        Console.WriteLine();

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var defaultPath = Path.Combine(desktop, $"zapret-settings-{DateTime.Now:yyyyMMdd_HHmm}.zip");
        var savePath = ConsoleMenu.Prompt("Путь для сохранения", defaultPath) ?? defaultPath;

        try
        {
            if (File.Exists(savePath)) File.Delete(savePath);

            using var zip = ZipFile.Open(savePath, ZipArchiveMode.Create);

            // config.json
            AddFileIfExists(zip, rootDir, "config.json");

            // utils/ — all settings files
            var utilsDir = Path.Combine(rootDir, "utils");
            if (Directory.Exists(utilsDir))
            {
                foreach (var f in Directory.GetFiles(utilsDir))
                    zip.CreateEntryFromFile(f, $"utils/{Path.GetFileName(f)}");
            }

            // profiles/
            var profilesDir = Path.Combine(rootDir, "profiles");
            if (Directory.Exists(profilesDir))
            {
                foreach (var f in Directory.GetFiles(profilesDir))
                    zip.CreateEntryFromFile(f, $"profiles/{Path.GetFileName(f)}");
            }

            // lists/ — user files only
            if (Directory.Exists(listsDir))
            {
                foreach (var f in Directory.GetFiles(listsDir, "*-user.txt"))
                    zip.CreateEntryFromFile(f, $"lists/{Path.GetFileName(f)}");
                foreach (var f in Directory.GetFiles(listsDir, "whitelist*.txt"))
                    zip.CreateEntryFromFile(f, $"lists/{Path.GetFileName(f)}");
                foreach (var f in Directory.GetFiles(listsDir, "blacklist*.txt"))
                    zip.CreateEntryFromFile(f, $"lists/{Path.GetFileName(f)}");
            }

            // strategies/custom_*.bat
            var strategiesDir = Path.Combine(rootDir, "strategies");
            if (Directory.Exists(strategiesDir))
            {
                foreach (var f in Directory.GetFiles(strategiesDir, "custom_*.bat"))
                    zip.CreateEntryFromFile(f, $"strategies/{Path.GetFileName(f)}");
            }

            // Meta
            var meta = new
            {
                version = "2.5.0",
                exported = DateTime.Now.ToString("o"),
                machine = Environment.MachineName,
                os = Environment.OSVersion.ToString()
            };
            var metaEntry = zip.CreateEntry("meta.json");
            using (var writer = new StreamWriter(metaEntry.Open()))
                writer.Write(JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));

            var size = new FileInfo(savePath).Length / 1024;
            ConsoleMenu.WriteOk($"Экспортировано: {savePath} ({size} KB)");
        }
        catch (Exception ex)
        {
            ConsoleMenu.WriteError($"Ошибка экспорта: {ex.Message}");
        }

        ConsoleMenu.PauseAny();
    }

    private static void Import(string rootDir, string listsDir)
    {
        Console.Clear();
        ConsoleMenu.WriteHeader("ИМПОРТ НАСТРОЕК");
        Console.WriteLine();

        var zipPath = ConsoleMenu.Prompt("Путь к ZIP файлу", "");
        if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
        {
            ConsoleMenu.WriteError("Файл не найден");
            ConsoleMenu.PauseAny();
            return;
        }

        try
        {
            using var zip = ZipFile.OpenRead(zipPath);

            // Check meta
            var metaEntry = zip.GetEntry("meta.json");
            if (metaEntry != null)
            {
                using var reader = new StreamReader(metaEntry.Open());
                var metaJson = reader.ReadToEnd();
                ConsoleMenu.WriteInfo($"Мета: {metaJson.Replace("\n", " ").Replace("\r", "")}");
            }

            Console.WriteLine();

            int imported = 0, skipped = 0;
            foreach (var entry in zip.Entries)
            {
                if (entry.Name == "" || entry.FullName == "meta.json") continue;

                var destPath = Path.Combine(rootDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                var destDir = Path.GetDirectoryName(destPath);
                if (destDir != null) Directory.CreateDirectory(destDir);

                // Protected files — ask before overwriting
                if (File.Exists(destPath) && entry.Name == "config.json")
                {
                    if (!ConsoleMenu.Confirm($"Перезаписать {entry.Name}?"))
                    {
                        skipped++;
                        continue;
                    }
                }

                entry.ExtractToFile(destPath, overwrite: true);
                imported++;
            }

            ConsoleMenu.WriteOk($"Импортировано: {imported} файлов, пропущено: {skipped}");
        }
        catch (Exception ex)
        {
            ConsoleMenu.WriteError($"Ошибка импорта: {ex.Message}");
        }

        ConsoleMenu.PauseAny();
    }

    private static void AddFileIfExists(ZipArchive zip, string rootDir, string relativePath)
    {
        var fullPath = Path.Combine(rootDir, relativePath);
        if (File.Exists(fullPath))
            zip.CreateEntryFromFile(fullPath, relativePath);
    }
}
