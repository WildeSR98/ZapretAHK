using System.Diagnostics;

namespace ZapretManager.Diagnostics;

/// <summary>
/// Adds Windows Defender exclusions to protect files from being deleted after archive extraction.
/// Protected files: winws.exe, WinDivert.dll, WinDivert64.sys, cygwin1.dll, TgWsProxy_windows.exe.
/// Requires administrator privileges (already granted via app.manifest).
/// </summary>
public static class AntivirusExcluder
{
    private static readonly string[] ProtectedBinFiles =
    {
        "winws.exe", "WinDivert.dll", "WinDivert64.sys", "cygwin1.dll"
    };

    // TgWsProxy_windows.exe is managed by TgProxyManager.EnsureInRootDir
    // and may be in project root rather than publish/, so skip file check here
    private static readonly string[] ProtectedRootFiles = Array.Empty<string>();

    private static readonly string[] ProtectedProcesses =
    {
        "winws.exe", "TgWsProxy_windows.exe"
    };

    /// <summary>
    /// Adds the folder to Windows Defender exclusion path via Add-MpPreference.
    /// </summary>
    public static bool AddFolderExclusion(string folderPath)
    {
        try
        {
            var psi = new ProcessStartInfo("powershell", 
                $"-NoProfile -Command \"Add-MpPreference -ExclusionPath '{folderPath}'\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true
            };
            var proc = Process.Start(psi);
            proc?.WaitForExit(10000);
            Core.Logger.Info($"Added Defender folder exclusion: {folderPath}");
            return proc?.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Core.Logger.Warn($"Failed to add folder exclusion: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Adds process exclusion to Windows Defender via Add-MpPreference.
    /// </summary>
    public static bool AddProcessExclusion(string processName)
    {
        try
        {
            var psi = new ProcessStartInfo("powershell",
                $"-NoProfile -Command \"Add-MpPreference -ExclusionProcess '{processName}'\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true
            };
            var proc = Process.Start(psi);
            proc?.WaitForExit(10000);
            Core.Logger.Info($"Added Defender process exclusion: {processName}");
            return proc?.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Core.Logger.Warn($"Failed to add process exclusion: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Checks which critical files are missing (possibly deleted by antivirus).
    /// </summary>
    public static List<string> CheckMissingFiles(string binDir, string rootDir)
    {
        var missing = new List<string>();

        foreach (var file in ProtectedBinFiles)
        {
            if (!File.Exists(Path.Combine(binDir, file)))
                missing.Add($"bin/{file}");
        }

        foreach (var file in ProtectedRootFiles)
        {
            if (!File.Exists(Path.Combine(rootDir, file)))
                missing.Add(file);
        }

        return missing;
    }

    /// <summary>
    /// Full protection: adds folder + process exclusions and checks for missing files.
    /// Returns a diagnostic result.
    /// </summary>
    public static DiagResult ProtectAndVerify(string rootDir, string binDir)
    {
        // Add folder exclusion for rootDir and parent dir (project root)
        bool folderOk = AddFolderExclusion(rootDir);
        var parentDir = Path.GetFullPath(Path.Combine(rootDir, ".."));
        if (parentDir != rootDir)
            AddFolderExclusion(parentDir);

        // Add process exclusions
        foreach (var proc in ProtectedProcesses)
        {
            AddProcessExclusion(proc);
        }

        // Check for missing files
        var missing = CheckMissingFiles(binDir, rootDir);

        if (missing.Count > 0)
        {
            return new DiagResult("Антивирус", DiagLevel.Warning,
                $"Исключения Defender добавлены, но отсутствуют файлы: {string.Join(", ", missing)}. " +
                "Распакуйте архив заново после добавления исключений.");
        }

        if (folderOk)
        {
            return new DiagResult("Антивирус", DiagLevel.Ok,
                $"Папка {rootDir} добавлена в исключения Windows Defender. Все файлы на месте.");
        }

        return new DiagResult("Антивирус", DiagLevel.Warning,
            "Не удалось добавить исключения в Windows Defender. Добавьте вручную через Безопасность Windows.");
    }
}
