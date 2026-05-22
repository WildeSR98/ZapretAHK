using System.Diagnostics;
using ZapretManager.Core;

namespace ZapretManager.Service;

/// <summary>
/// Creates/removes Windows Task Scheduler tasks for background update checking.
/// </summary>
public static class TaskSchedulerHelper
{
    private const string TaskName = "ZapretManagerUpdateCheck";

    /// <summary>Create a scheduled task that runs the manager with --check-updates every hour.</summary>
    public static bool CreateUpdateTask(string managerExePath)
    {
        try
        {
            // Remove old task if exists
            RemoveUpdateTask();

            // schtasks /create with hourly trigger + logon trigger
            var xml = GenerateTaskXml(managerExePath);
            var tempXml = Path.Combine(Path.GetTempPath(), "zapret_task.xml");
            File.WriteAllText(tempXml, xml, System.Text.Encoding.Unicode);

            var psi = new ProcessStartInfo("schtasks",
                $"/create /tn \"{TaskName}\" /xml \"{tempXml}\" /f")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(10000);

            try { File.Delete(tempXml); } catch { }

            if (proc?.ExitCode == 0)
            {
                Core.Logger.Ok($"Задача планировщика создана: {TaskName}");
                return true;
            }
            else
            {
                var err = proc?.StandardError.ReadToEnd();
                Core.Logger.Error($"Не удалось создать задачу: {err}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Core.Logger.Error($"TaskScheduler: {ex.Message}");
            return false;
        }
    }

    /// <summary>Remove the scheduled update task.</summary>
    public static bool RemoveUpdateTask()
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks",
                $"/delete /tn \"{TaskName}\" /f")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(5000);
            return proc?.ExitCode == 0;
        }
        catch (Exception ex) { Logger.Error($"[TaskSchedulerHelper] {ex.GetType().Name}: {ex.Message}"); return false; }
    }

    /// <summary>Check if the update task exists.</summary>
    public static bool TaskExists()
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks",
                $"/query /tn \"{TaskName}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(5000);
            return proc?.ExitCode == 0;
        }
        catch (Exception ex) { Logger.Error($"[TaskSchedulerHelper] {ex.GetType().Name}: {ex.Message}"); return false; }
    }

    private static string GenerateTaskXml(string exePath)
    {
        var dir = Path.GetDirectoryName(exePath) ?? "";
        return $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Zapret Manager - фоновая проверка обновлений</Description>
  </RegistrationInfo>
  <Triggers>
    <TimeTrigger>
      <Repetition>
        <Interval>PT1H</Interval>
        <StopAtDurationEnd>false</StopAtDurationEnd>
      </Repetition>
      <StartBoundary>2024-01-01T00:00:00</StartBoundary>
      <Enabled>true</Enabled>
    </TimeTrigger>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <Delay>PT30S</Delay>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal>
      <RunLevel>HighestAvailable</RunLevel>
      <LogonType>InteractiveToken</LogonType>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <ExecutionTimeLimit>PT1M</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions>
    <Exec>
      <Command>""{exePath}""</Command>
      <Arguments>--check-updates</Arguments>
      <WorkingDirectory>{dir}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>";
    }

    /// <summary>Get the next scheduled run time, or null if task not found.</summary>
    public static DateTime? GetNextRunTime() => QueryTaskTime("Next Run Time", "\u0421\u043b\u0435\u0434\u0443\u044e\u0449\u0435\u0435 \u0432\u0440\u0435\u043c\u044f");

    /// <summary>Get the last run time, or null if task not found.</summary>
    public static DateTime? GetLastRunTime() => QueryTaskTime("Last Run Time", "\u041f\u043e\u0441\u043b\u0435\u0434\u043d\u0435\u0435 \u0432\u0440\u0435\u043c\u044f");

    private static DateTime? QueryTaskTime(string enKey, string ruKey)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks",
                $"/query /tn \"{TaskName}\" /fo LIST /v")
            {
                CreateNoWindow = true, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            var output = proc?.StandardOutput.ReadToEnd() ?? "";
            proc?.WaitForExit(5000);
            foreach (var line in output.Split('\n'))
            {
                if (!line.Contains(enKey) && !line.Contains(ruKey)) continue;
                var idx = line.IndexOf(':');
                if (idx < 0) continue;
                var raw = line[(idx + 1)..].Trim();
                if (DateTime.TryParse(raw, out var dt)) return dt;
            }
            return null;
        }
        catch (Exception ex) { Logger.Error($"[TaskSchedulerHelper] {ex.GetType().Name}: {ex.Message}"); return null; }
    }
}
