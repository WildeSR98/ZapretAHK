using System.Diagnostics;

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
        catch { return false; }
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
        catch { return false; }
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
}
