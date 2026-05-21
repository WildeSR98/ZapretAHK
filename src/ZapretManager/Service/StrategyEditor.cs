using ZapretManager.Core;
using ZapretManager.UI;

namespace ZapretManager.Service;

/// <summary>
/// Interactive strategy editor — create and modify .bat files for winws.
/// </summary>
public static class StrategyEditor
{
    // Supported winws parameters
    private static readonly string[] DesyncMethods =
        { "fake", "disorder", "disorder2", "split", "split2", "ipfrag2", "hopbyhop", "destopt", "multisplit" };
    private static readonly string[] FoolingMethods =
        { "none", "md5sig", "ts", "badseq", "badsum", "datanoack", "hopbyhop", "hopbyhop2" };

    public static void Run(string strategiesDir, string binDir, string listsDir)
    {
        Console.Clear();
        ConsoleMenu.WriteHeader("РЕДАКТОР СТРАТЕГИЙ");
        Console.WriteLine();

        Console.WriteLine("   1. Создать новую стратегию");
        Console.WriteLine("   2. Редактировать существующую");
        Console.WriteLine("   0. Назад");
        Console.WriteLine();

        var choice = ConsoleMenu.Prompt("Выберите", "0");
        switch (choice)
        {
            case "1": CreateNew(strategiesDir, binDir, listsDir); break;
            case "2": EditExisting(strategiesDir, binDir, listsDir); break;
        }
    }

    private static void CreateNew(string strategiesDir, string binDir, string listsDir)
    {
        Console.Clear();
        ConsoleMenu.WriteHeader("СОЗДАНИЕ СТРАТЕГИИ");
        Console.WriteLine();

        // Choose template
        var templates = Directory.GetFiles(strategiesDir, "general*.bat");
        if (templates.Length > 0)
        {
            ConsoleMenu.WriteInfo("Доступные шаблоны:");
            for (int i = 0; i < templates.Length; i++)
                Console.WriteLine($"      {i + 1}. {Path.GetFileName(templates[i])}");
            Console.WriteLine($"      0. С нуля");
            Console.WriteLine();

            var tmplChoice = ConsoleMenu.Prompt("Базовый шаблон", "0");
            if (int.TryParse(tmplChoice, out var tmplIdx) && tmplIdx > 0 && tmplIdx <= templates.Length)
            {
                EditStrategy(templates[tmplIdx - 1], strategiesDir, binDir, listsDir, isNew: true);
                return;
            }
        }

        // From scratch
        BuildFromScratch(strategiesDir, binDir, listsDir);
    }

    private static void EditExisting(string strategiesDir, string binDir, string listsDir)
    {
        Console.Clear();
        ConsoleMenu.WriteHeader("РЕДАКТИРОВАНИЕ СТРАТЕГИИ");
        Console.WriteLine();

        var files = Directory.GetFiles(strategiesDir, "*.bat");
        if (files.Length == 0)
        {
            ConsoleMenu.WriteWarn("Нет .bat файлов в strategies/");
            ConsoleMenu.PauseAny();
            return;
        }

        for (int i = 0; i < files.Length; i++)
            Console.WriteLine($"      {i + 1}. {Path.GetFileName(files[i])}");
        Console.WriteLine();

        var input = ConsoleMenu.Prompt("Выберите файл", "");
        if (int.TryParse(input, out var idx) && idx > 0 && idx <= files.Length)
        {
            EditStrategy(files[idx - 1], strategiesDir, binDir, listsDir, isNew: false);
        }
    }

    private static void EditStrategy(string batPath, string strategiesDir, string binDir, string listsDir, bool isNew)
    {
        var lines = File.ReadAllLines(batPath);
        var winwsLines = lines.Where(l => l.Contains("winws") && !l.TrimStart().StartsWith("rem") && !l.TrimStart().StartsWith("::")).ToList();

        if (winwsLines.Count == 0)
        {
            ConsoleMenu.WriteWarn("Не найдены команды winws в файле");
            ConsoleMenu.PauseAny();
            return;
        }

        Console.Clear();
        ConsoleMenu.WriteHeader(isNew ? "НОВАЯ СТРАТЕГИЯ (на базе шаблона)" : $"РЕДАКТИРОВАНИЕ: {Path.GetFileName(batPath)}");
        Console.WriteLine();

        // Parse parameters from first winws line
        var mainLine = winwsLines[0];
        var currentParams = ParseWinwsParams(mainLine);

        // Interactive editing
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("   Текущие параметры:");
        Console.ResetColor();
        foreach (var kv in currentParams)
            Console.WriteLine($"      {kv.Key} = {kv.Value}");

        Console.WriteLine();
        Console.WriteLine("   Что изменить?");
        Console.WriteLine("      1. DPI-desync метод");
        Console.WriteLine("      2. TTL значение");
        Console.WriteLine("      3. Fooling метод");
        Console.WriteLine("      4. TCP/UDP порты");
        Console.WriteLine("      5. Сохранить как есть");
        Console.WriteLine();

        bool modified = false;
        var newParams = new Dictionary<string, string>(currentParams);

        while (true)
        {
            var edit = ConsoleMenu.Prompt("Параметр (1-5)", "5");
            switch (edit)
            {
                case "1":
                    Console.WriteLine("   Методы: " + string.Join(", ", DesyncMethods));
                    var method = ConsoleMenu.Prompt("   dpi-desync", newParams.GetValueOrDefault("--dpi-desync", "fake"));
                    if (method != null) { newParams["--dpi-desync"] = method; modified = true; }
                    break;

                case "2":
                    var ttl = ConsoleMenu.Prompt("   dpi-desync-ttl (1-12)", newParams.GetValueOrDefault("--dpi-desync-ttl", "6"));
                    if (ttl != null) { newParams["--dpi-desync-ttl"] = ttl; modified = true; }
                    break;

                case "3":
                    Console.WriteLine("   Методы: " + string.Join(", ", FoolingMethods));
                    var fool = ConsoleMenu.Prompt("   dpi-desync-fooling", newParams.GetValueOrDefault("--dpi-desync-fooling", "none"));
                    if (fool != null) { newParams["--dpi-desync-fooling"] = fool; modified = true; }
                    break;

                case "4":
                    var tcp = ConsoleMenu.Prompt("   wf-tcp (порты)", newParams.GetValueOrDefault("--wf-tcp", "80,443"));
                    if (tcp != null) { newParams["--wf-tcp"] = tcp; modified = true; }
                    var udp = ConsoleMenu.Prompt("   wf-udp (порты)", newParams.GetValueOrDefault("--wf-udp", "443,50000-50099"));
                    if (udp != null) { newParams["--wf-udp"] = udp; modified = true; }
                    break;

                case "5":
                    goto done;
            }
        }
        done:

        if (!modified && !isNew)
        {
            ConsoleMenu.WriteInfo("Без изменений");
            ConsoleMenu.PauseAny();
            return;
        }

        // Build new command line
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("   Итоговые параметры:");
        Console.ResetColor();
        foreach (var kv in newParams)
            Console.WriteLine($"      {kv.Key} = {kv.Value}");

        // Save
        string saveName;
        if (isNew)
        {
            saveName = ConsoleMenu.Prompt("Имя файла (без .bat)", $"custom_{DateTime.Now:yyyyMMdd_HHmm}") ?? $"custom_{DateTime.Now:yyyyMMdd}";
            saveName = saveName.Replace(".bat", "");
        }
        else
        {
            saveName = Path.GetFileNameWithoutExtension(batPath);
        }

        var savePath = Path.Combine(strategiesDir, saveName + ".bat");

        if (ConsoleMenu.Confirm($"Сохранить как {saveName}.bat?"))
        {
            // Rebuild bat content
            var newLine = BuildWinwsCommand(newParams, binDir, listsDir);
            var newContent = isNew
                ? $"@echo off\r\nstart /min \"\" {newLine}\r\n"
                : string.Join("\r\n", lines.Select(l =>
                    l.Contains("winws") && !l.TrimStart().StartsWith("rem") ? ReplaceWinwsLine(l, newParams) : l));

            File.WriteAllText(savePath, newContent);
            ConsoleMenu.WriteOk($"Сохранено: {savePath}");
        }

        ConsoleMenu.PauseAny();
    }

    private static void BuildFromScratch(string strategiesDir, string binDir, string listsDir)
    {
        Console.Clear();
        ConsoleMenu.WriteHeader("СОЗДАНИЕ С НУЛЯ");
        Console.WriteLine();

        var p = new Dictionary<string, string>();

        Console.WriteLine("   Методы desync: " + string.Join(", ", DesyncMethods));
        p["--dpi-desync"] = ConsoleMenu.Prompt("   dpi-desync", "fake") ?? "fake";
        p["--dpi-desync-ttl"] = ConsoleMenu.Prompt("   dpi-desync-ttl", "6") ?? "6";

        Console.WriteLine("   Методы fooling: " + string.Join(", ", FoolingMethods));
        p["--dpi-desync-fooling"] = ConsoleMenu.Prompt("   dpi-desync-fooling", "md5sig") ?? "md5sig";

        p["--wf-tcp"] = ConsoleMenu.Prompt("   wf-tcp порты", "80,443") ?? "80,443";
        p["--wf-udp"] = ConsoleMenu.Prompt("   wf-udp порты", "443,50000-50099") ?? "443,50000-50099";

        Console.WriteLine();
        var name = ConsoleMenu.Prompt("Имя файла (без .bat)", $"custom_{DateTime.Now:yyyyMMdd_HHmm}") ?? $"custom_{DateTime.Now:yyyyMMdd}";
        var savePath = Path.Combine(strategiesDir, name + ".bat");

        var cmd = BuildWinwsCommand(p, binDir, listsDir);
        var content = $"@echo off\r\nstart /min \"\" {cmd}\r\n";

        File.WriteAllText(savePath, content);
        ConsoleMenu.WriteOk($"Создано: {savePath}");
        ConsoleMenu.PauseAny();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Dictionary<string, string> ParseWinwsParams(string line)
    {
        var result = new Dictionary<string, string>();
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].StartsWith("--"))
            {
                var key = parts[i];
                if (i + 1 < parts.Length && !parts[i + 1].StartsWith("--"))
                {
                    result[key] = parts[i + 1];
                    i++;
                }
                else
                {
                    result[key] = "true";
                }
            }
        }

        return result;
    }

    private static string BuildWinwsCommand(Dictionary<string, string> p, string binDir, string listsDir)
    {
        var parts = new List<string> { $"\"{Path.Combine(binDir, "winws.exe")}\"" };

        foreach (var kv in p)
        {
            if (kv.Value == "true" || kv.Value == "none")
                parts.Add(kv.Key);
            else
                parts.Add($"{kv.Key} {kv.Value}");
        }

        return string.Join(" ", parts);
    }

    private static string ReplaceWinwsLine(string line, Dictionary<string, string> newParams)
    {
        // Simple: rebuild the winws part with new params
        var prefix = "";
        var idx = line.IndexOf("winws", StringComparison.OrdinalIgnoreCase);
        if (idx > 0) prefix = line[..idx];

        var parts = new List<string> { prefix + "winws.exe" };
        foreach (var kv in newParams)
        {
            if (kv.Value == "true")
                parts.Add(kv.Key);
            else
                parts.Add($"{kv.Key} {kv.Value}");
        }

        return string.Join(" ", parts);
    }
}
