using ZapretManager.Core;
using ZapretManager.UI;

namespace ZapretManager.Service;

/// <summary>
/// Manages whitelist/blacklist domain files for winws --hostlist / --hostlist-exclude.
/// </summary>
public static class DomainManager
{
    private const string WhitelistFile = "whitelist-user.txt";
    private const string BlacklistFile = "blacklist-user.txt";

    public static void Run(string listsDir)
    {
        while (true)
        {
            Console.Clear();
            ConsoleMenu.WriteHeader("УПРАВЛЕНИЕ ДОМЕНАМИ");
            Console.WriteLine();

            var wlPath = Path.Combine(listsDir, WhitelistFile);
            var blPath = Path.Combine(listsDir, BlacklistFile);
            var wlCount = File.Exists(wlPath) ? File.ReadAllLines(wlPath).Count(l => !string.IsNullOrWhiteSpace(l)) : 0;
            var blCount = File.Exists(blPath) ? File.ReadAllLines(blPath).Count(l => !string.IsNullOrWhiteSpace(l)) : 0;

            Console.WriteLine($"   Whitelist (обходить):     {wlCount} доменов  [{WhitelistFile}]");
            Console.WriteLine($"   Blacklist (не обходить):  {blCount} доменов  [{BlacklistFile}]");
            Console.WriteLine();
            Console.WriteLine("   1. Показать whitelist");
            Console.WriteLine("   2. Показать blacklist");
            Console.WriteLine("   3. Добавить в whitelist");
            Console.WriteLine("   4. Добавить в blacklist");
            Console.WriteLine("   5. Удалить из whitelist");
            Console.WriteLine("   6. Удалить из blacklist");
            Console.WriteLine("   0. Назад");
            Console.WriteLine();

            var choice = ConsoleMenu.Prompt("Выберите", "0");
            switch (choice)
            {
                case "1": ShowDomains(wlPath, "WHITELIST"); break;
                case "2": ShowDomains(blPath, "BLACKLIST"); break;
                case "3": AddDomain(wlPath, "whitelist"); break;
                case "4": AddDomain(blPath, "blacklist"); break;
                case "5": RemoveDomain(wlPath, "whitelist"); break;
                case "6": RemoveDomain(blPath, "blacklist"); break;
                case "0": return;
            }
        }
    }

    private static void ShowDomains(string path, string label)
    {
        Console.Clear();
        ConsoleMenu.WriteHeader(label);
        Console.WriteLine();

        if (!File.Exists(path))
        {
            ConsoleMenu.WriteInfo("Файл не создан (пуст)");
        }
        else
        {
            var domains = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            if (domains.Count == 0)
                ConsoleMenu.WriteInfo("Список пуст");
            else
                foreach (var d in domains)
                    Console.WriteLine($"      • {d}");
        }

        Console.WriteLine();
        ConsoleMenu.PauseAny();
    }

    private static void AddDomain(string path, string listName)
    {
        Console.WriteLine();
        var domain = ConsoleMenu.Prompt($"Домен для добавления в {listName}", "");
        if (string.IsNullOrWhiteSpace(domain)) return;

        // Validate
        domain = domain.Trim().ToLower();
        domain = domain.Replace("https://", "").Replace("http://", "").TrimEnd('/');

        // Remove path
        if (domain.Contains('/')) domain = domain.Split('/')[0];

        if (!domain.Contains('.'))
        {
            ConsoleMenu.WriteError("Некорректный домен");
            return;
        }

        // Check for duplicates
        var existing = File.Exists(path) ? File.ReadAllLines(path) : Array.Empty<string>();
        if (existing.Any(l => l.Trim().Equals(domain, StringComparison.OrdinalIgnoreCase)))
        {
            ConsoleMenu.WriteWarn($"{domain} уже в {listName}");
            return;
        }

        File.AppendAllText(path, domain + Environment.NewLine);
        ConsoleMenu.WriteOk($"Добавлено: {domain} → {listName}");
    }

    private static void RemoveDomain(string path, string listName)
    {
        if (!File.Exists(path))
        {
            ConsoleMenu.WriteInfo("Список пуст");
            return;
        }

        var domains = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (domains.Count == 0)
        {
            ConsoleMenu.WriteInfo("Список пуст");
            return;
        }

        Console.WriteLine();
        for (int i = 0; i < domains.Count; i++)
            Console.WriteLine($"      {i + 1}. {domains[i]}");
        Console.WriteLine();

        var input = ConsoleMenu.Prompt("Номер для удаления", "");
        if (int.TryParse(input, out var idx) && idx > 0 && idx <= domains.Count)
        {
            var removed = domains[idx - 1];
            domains.RemoveAt(idx - 1);
            File.WriteAllLines(path, domains);
            ConsoleMenu.WriteOk($"Удалено: {removed}");
        }
    }
}
