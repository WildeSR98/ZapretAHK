using System.Runtime.InteropServices;
using ZapretManager.Core;

namespace ZapretManager.Service;

/// <summary>Windows Service management via P/Invoke — no sc.exe dependency.</summary>
public static class WinServiceManager
{
    // ── P/Invoke ──────────────────────────────────────────────────────────────
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManager(string? machine, string? database, uint access);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateService(IntPtr scm, string name, string displayName,
        uint access, uint type, uint start, uint error,
        string binPath, string? group, IntPtr tag, string? deps,
        string? account, string? password);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenService(IntPtr scm, string name, uint access);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool StartService(IntPtr svc, uint argc, string[]? argv);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DeleteService(IntPtr svc);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool ControlService(IntPtr svc, uint ctrl, ref SERVICE_STATUS status);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatus(IntPtr svc, ref SERVICE_STATUS status);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr handle);

    // Overload for description (infoLevel=1)
    [DllImport("advapi32.dll", EntryPoint = "ChangeServiceConfig2W", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ChangeServiceConfig2(IntPtr svc,
        uint infoLevel, ref SERVICE_DESCRIPTIONW desc);

    // Overload for failure actions (infoLevel=2)
    [DllImport("advapi32.dll", EntryPoint = "ChangeServiceConfig2W", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ChangeServiceConfig2Failure(IntPtr svc,
        uint infoLevel, IntPtr failureActions);

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public uint Type, CurrentState, ControlsAccepted, Win32ExitCode,
                    ServiceExitCode, CheckPoint, WaitHint;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SERVICE_DESCRIPTIONW
    {
        public string Description;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SC_ACTION
    {
        public uint Type;  // SC_ACTION_RESTART = 1
        public uint Delay; // milliseconds
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SERVICE_FAILURE_ACTIONS
    {
        public uint ResetPeriod; // seconds (86400 = 1 day)
        public string? RebootMsg;
        public string? Command;
        public uint ActionsCount;
        public IntPtr Actions;
    }

    private const uint SC_MANAGER_ALL = 0xF003F;
    private const uint SERVICE_ALL    = 0xF01FF;
    private const uint SERVICE_WIN32_OWN_PROCESS = 0x10;
    private const uint SERVICE_AUTO_START         = 0x02;
    private const uint SERVICE_DEMAND_START       = 0x03;
    private const uint SERVICE_ERROR_NORMAL       = 0x01;
    private const uint SERVICE_CONTROL_STOP       = 0x01;
    private const uint SC_ACTION_RESTART          = 1;
    private const uint SERVICE_CONFIG_DESCRIPTION = 1;
    private const uint SERVICE_CONFIG_FAILURE_ACTIONS = 2;

    public enum ServiceState { NotInstalled, Stopped, Starting, Running, Stopping, Unknown }

    // ── Public API ────────────────────────────────────────────────────────────

    public static bool Install(string name, string displayName, string description,
        string binPathWithArgs, bool autoStart = true)
    {
        var scm = OpenSCManager(null, null, SC_MANAGER_ALL);
        if (scm == IntPtr.Zero) return false;
        try
        {
            // Delete old if exists
            var old = OpenService(scm, name, SERVICE_ALL);
            if (old != IntPtr.Zero)
            {
                var st = new SERVICE_STATUS();
                ControlService(old, SERVICE_CONTROL_STOP, ref st);
                Thread.Sleep(1000);
                DeleteService(old);
                CloseServiceHandle(old);
                Thread.Sleep(2000); // Wait for Windows to fully release the service
            }

            var startType = autoStart ? SERVICE_AUTO_START : SERVICE_DEMAND_START;

            // Dependencies: wait for network stack (Tcpip) and sockets (Afd)
            // Multi-string format: each dep separated by \0, terminated by \0\0
            string dependencies = "Tcpip\0Afd\0";

            var svc = CreateService(scm, name, displayName, SERVICE_ALL,
                SERVICE_WIN32_OWN_PROCESS, startType, SERVICE_ERROR_NORMAL,
                binPathWithArgs, null, IntPtr.Zero, dependencies, null, null);

            if (svc == IntPtr.Zero)
            {
                var err = Marshal.GetLastWin32Error();
                Core.Logger.Error($"CreateService failed: error {err}");
                return false;
            }

            // Set description
            var desc = new SERVICE_DESCRIPTIONW { Description = description };
            ChangeServiceConfig2(svc, SERVICE_CONFIG_DESCRIPTION, ref desc);

            // Set recovery policy: restart on crash (5s, 30s, 60s)
            SetRecoveryPolicy(svc);

            // Start service
            bool started = StartService(svc, 0, null);
            if (!started)
            {
                Core.Logger.Warn($"StartService failed for {name}, error: {Marshal.GetLastWin32Error()}");
            }

            // Poll until running or timeout (10 seconds, 500ms intervals)
            bool running = WaitForState(svc, 4 /* RUNNING */, 10000);
            CloseServiceHandle(svc);

            if (running)
            {
                Core.Logger.Ok($"Служба установлена и запущена: {name}");
                return true;
            }
            else
            {
                var state = GetState(name);
                Core.Logger.Warn($"Служба {name} установлена, но не запущена (state={state})");
                return false;
            }
        }
        finally { CloseServiceHandle(scm); }
    }

    public static bool Remove(string name)
    {
        var scm = OpenSCManager(null, null, SC_MANAGER_ALL);
        if (scm == IntPtr.Zero) return false;
        try
        {
            var svc = OpenService(scm, name, SERVICE_ALL);
            if (svc == IntPtr.Zero) return true; // already gone
            var st = new SERVICE_STATUS();
            ControlService(svc, SERVICE_CONTROL_STOP, ref st);
            Thread.Sleep(500);
            var ok = DeleteService(svc);
            CloseServiceHandle(svc);
            if (ok) Core.Logger.Ok($"Служба удалена: {name}");
            return ok;
        }
        finally { CloseServiceHandle(scm); }
    }

    public static ServiceState GetState(string name)
    {
        var scm = OpenSCManager(null, null, 0x0001);
        if (scm == IntPtr.Zero) return ServiceState.Unknown;
        try
        {
            var svc = OpenService(scm, name, 0x0004); // SERVICE_QUERY_STATUS
            if (svc == IntPtr.Zero) return ServiceState.NotInstalled;
            var st = new SERVICE_STATUS();
            if (!QueryServiceStatus(svc, ref st)) { CloseServiceHandle(svc); return ServiceState.Unknown; }
            CloseServiceHandle(svc);
            return st.CurrentState switch
            {
                1 => ServiceState.Stopped,
                2 => ServiceState.Starting,
                3 => ServiceState.Stopping,
                4 => ServiceState.Running,
                _ => ServiceState.Unknown
            };
        }
        finally { CloseServiceHandle(scm); }
    }

    public static bool Start(string name)
    {
        var scm = OpenSCManager(null, null, SC_MANAGER_ALL);
        if (scm == IntPtr.Zero) return false;
        try
        {
            var svc = OpenService(scm, name, SERVICE_ALL);
            if (svc == IntPtr.Zero) return false;
            var ok = StartService(svc, 0, null);
            CloseServiceHandle(svc);
            return ok;
        }
        finally { CloseServiceHandle(scm); }
    }

    public static bool Stop(string name)
    {
        var scm = OpenSCManager(null, null, SC_MANAGER_ALL);
        if (scm == IntPtr.Zero) return false;
        try
        {
            var svc = OpenService(scm, name, SERVICE_ALL);
            if (svc == IntPtr.Zero) return false;
            var st = new SERVICE_STATUS();
            var ok = ControlService(svc, SERVICE_CONTROL_STOP, ref st);
            CloseServiceHandle(svc);
            return ok;
        }
        finally { CloseServiceHandle(scm); }
    }

    /// <summary>Get the ImagePath (binPath) of the service from registry.</summary>
    public static string? GetImagePath(string name)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                $@"System\CurrentControlSet\Services\{name}");
            return key?.GetValue("ImagePath")?.ToString();
        }
        catch (Exception ex) { Logger.Error($"[WinServiceManager] {ex.GetType().Name}: {ex.Message}"); return null; }
    }

    /// <summary>Update the ImagePath if the install directory has moved.</summary>
    public static bool RepairBinPath(string name, string newBinPathWithArgs)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                $@"System\CurrentControlSet\Services\{name}", writable: true);
            if (key == null) return false;
            key.SetValue("ImagePath", newBinPathWithArgs);
            Core.Logger.Info($"ImagePath обновлён для {name}");
            return true;
        }
        catch (Exception ex)
        {
            Core.Logger.Error($"RepairBinPath failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Verify the service is healthy: running and binPath matches current directory.</summary>
    public static (bool IsHealthy, string Message) VerifyServiceHealth(string name, string expectedBinDir)
    {
        var state = GetState(name);
        if (state == ServiceState.NotInstalled)
            return (false, "Служба не установлена");

        var imagePath = GetImagePath(name);
        if (imagePath == null)
            return (false, "Не удалось прочитать ImagePath");

        // Check if the path points to the current directory
        var expectedWinws = Path.Combine(expectedBinDir, "winws.exe");
        bool pathOk = imagePath.Contains(expectedWinws, StringComparison.OrdinalIgnoreCase)
                   || imagePath.Contains($"\"{expectedWinws}\"", StringComparison.OrdinalIgnoreCase);

        if (!pathOk)
            return (false, $"ImagePath указывает на другую папку. Текущий: {imagePath[..Math.Min(80, imagePath.Length)]}...");

        if (state != ServiceState.Running)
            return (false, $"Служба установлена, но не запущена (состояние: {state})");

        return (true, "Служба запущена и путь корректен");
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>Set recovery policy: restart service on failure (5s, 30s, 60s delays).</summary>
    private static void SetRecoveryPolicy(IntPtr svc)
    {
        var actions = new SC_ACTION[]
        {
            new() { Type = SC_ACTION_RESTART, Delay = 5000 },   // 1st failure: restart after 5s
            new() { Type = SC_ACTION_RESTART, Delay = 30000 },  // 2nd failure: restart after 30s
            new() { Type = SC_ACTION_RESTART, Delay = 60000 },  // 3rd failure: restart after 60s
        };

        var actionsSize = Marshal.SizeOf<SC_ACTION>() * actions.Length;
        var actionsPtr = Marshal.AllocHGlobal(actionsSize);

        try
        {
            for (int i = 0; i < actions.Length; i++)
                Marshal.StructureToPtr(actions[i], actionsPtr + i * Marshal.SizeOf<SC_ACTION>(), false);

            var failureActions = new SERVICE_FAILURE_ACTIONS
            {
                ResetPeriod = 86400, // Reset failure count after 1 day
                RebootMsg = null,
                Command = null,
                ActionsCount = (uint)actions.Length,
                Actions = actionsPtr
            };

            var faSize = Marshal.SizeOf<SERVICE_FAILURE_ACTIONS>();
            var faPtr = Marshal.AllocHGlobal(faSize);
            try
            {
                Marshal.StructureToPtr(failureActions, faPtr, false);
                var ok = ChangeServiceConfig2Failure(svc, SERVICE_CONFIG_FAILURE_ACTIONS, faPtr);
                if (ok)
                    Core.Logger.Info("Recovery policy установлена: restart 5s/30s/60s");
                else
                    Core.Logger.Warn($"SetRecoveryPolicy failed: error {Marshal.GetLastWin32Error()}");
            }
            finally { Marshal.FreeHGlobal(faPtr); }
        }
        finally { Marshal.FreeHGlobal(actionsPtr); }
    }

    /// <summary>Poll service state until it reaches targetState or timeout.</summary>
    private static bool WaitForState(IntPtr svc, uint targetState, int timeoutMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var st = new SERVICE_STATUS();
            if (QueryServiceStatus(svc, ref st) && st.CurrentState == targetState)
                return true;
            Thread.Sleep(500);
        }
        return false;
    }
}
