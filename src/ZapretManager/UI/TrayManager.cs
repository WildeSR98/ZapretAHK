using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ZapretManager.Core;
using ZapretManager.Service;

namespace ZapretManager.UI;

/// <summary>
/// System tray (NotifyIcon) manager for background mode.
/// Provides status icon, context menu with service control.
/// </summary>
public sealed class TrayManager : IDisposable
{
    private NotifyIcon? _icon;
    private System.Threading.Timer? _pollTimer;
    private readonly string _rootDir;
    private readonly AppConfig _cfg;
    private bool _disposed;

    // Console window P/Invoke
    [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    public TrayManager(string rootDir, AppConfig cfg)
    {
        _rootDir = rootDir;
        _cfg = cfg;
    }

    public void Start()
    {
        var thread = new Thread(() =>
        {
            _icon = new NotifyIcon
            {
                Icon = CreateStatusIcon(ConsoleColor.Gray),
                Text = "Zapret Manager — загрузка...",
                Visible = true,
                ContextMenuStrip = BuildMenu()
            };

            _icon.DoubleClick += (_, _) => ShowConsole();

            // Poll status every N seconds
            var interval = Math.Max(_cfg.Tray.StatusPollIntervalSec, 5) * 1000;
            _pollTimer = new System.Threading.Timer(_ => UpdateStatus(), null, 0, interval);

            Application.Run();
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public void Stop()
    {
        _pollTimer?.Dispose();
        if (_icon != null)
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
        try { Application.ExitThread(); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    // ── Status Polling ──────────────────────────────────────────────────────

    private void UpdateStatus()
    {
        try
        {
            var state = WinServiceManager.GetState("zapret");
            var strategy = GetCurrentStrategy();
            var color = state switch
            {
                WinServiceManager.ServiceState.Running => ConsoleColor.Green,
                WinServiceManager.ServiceState.Stopped => ConsoleColor.Yellow,
                _ => ConsoleColor.Red
            };

            var stateText = state switch
            {
                WinServiceManager.ServiceState.Running => "Запущена",
                WinServiceManager.ServiceState.Stopped => "Остановлена",
                WinServiceManager.ServiceState.NotInstalled => "Не установлена",
                _ => state.ToString()
            };

            var tooltip = $"Zapret Manager\nСлужба: {stateText}\nСтратегия: {strategy}";
            if (tooltip.Length > 63) tooltip = tooltip[..63]; // NotifyIcon limit

            if (_icon != null)
            {
                _icon.Icon = CreateStatusIcon(color);
                _icon.Text = tooltip;
            }
        }
        catch (Exception ex) { Logger.Error($"[TrayManager] {ex.GetType().Name}: {ex.Message}"); /* silent */ }
    }

    // ── Context Menu ────────────────────────────────────────────────────────

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        // Status (non-clickable)
        var statusItem = new ToolStripMenuItem("Статус: загрузка...") { Enabled = false };
        menu.Items.Add(statusItem);

        var stratItem = new ToolStripMenuItem("Стратегия: ...") { Enabled = false };
        menu.Items.Add(stratItem);

        menu.Items.Add(new ToolStripSeparator());

        // Strategy submenu
        var stratMenu = new ToolStripMenuItem("Переключить стратегию");
        PopulateStrategies(stratMenu);
        menu.Items.Add(stratMenu);

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Перезапустить службу", null, (_, _) => RestartService());
        menu.Items.Add("Остановить службу", null, (_, _) => StopService());

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Открыть консоль", null, (_, _) =>
        {
            // Restart the process with --menu
            try
            {
                var exePath = Environment.ProcessPath ?? System.Reflection.Assembly.GetEntryAssembly()?.Location;
                if (exePath != null)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = "--menu",
                        UseShellExecute = true
                    });
                }
                Stop();
                Environment.Exit(0);
            }
            catch { ShowConsole(); }
        });

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Выход", null, (_, _) =>
        {
            Stop();
            Environment.Exit(0);
        });

        // Update status items on opening
        menu.Opening += (_, _) =>
        {
            var state = WinServiceManager.GetState("zapret");
            statusItem.Text = $"Служба: {state}";
            stratItem.Text = $"Стратегия: {GetCurrentStrategy()}";
        };

        return menu;
    }

    private void PopulateStrategies(ToolStripMenuItem parent)
    {
        var strategiesDir = Path.Combine(_rootDir, "strategies");
        if (!Directory.Exists(strategiesDir)) return;

        var files = Directory.GetFiles(strategiesDir, "general*.bat");
        foreach (var file in files.OrderBy(f => f))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            parent.DropDownItems.Add(name, null, (_, _) => SwitchStrategy(file));
        }
    }

    // ── Actions ─────────────────────────────────────────────────────────────

    private void SwitchStrategy(string batPath)
    {
        try
        {
            var state = WinServiceManager.GetState("zapret");
            if (state == WinServiceManager.ServiceState.Running)
                WinServiceManager.Stop("zapret");

            ProcessManager.KillAll();

            var winwsExe = Path.Combine(_rootDir, "bin", "winws.exe");
            var listsDir = Path.Combine(_rootDir, "lists");
            var utilsDir = Path.Combine(_rootDir, "utils");
            var gf = GameFilter.Get(utilsDir);
            var batArgs = StrategyReader.ParseArgs(batPath, Path.Combine(_rootDir, "bin"), listsDir, gf.Tcp, gf.Udp);
            var binPath = $"\"{winwsExe}\" {batArgs}";

            // Remove old and install new
            WinServiceManager.Remove("zapret");
            if (WinServiceManager.Install("zapret", "zapret", "Zapret DPI bypass", binPath))
            {
                // Save strategy name
                try
                {
                    using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(
                        @"System\CurrentControlSet\Services\zapret");
                    key?.SetValue("zapret-discord-youtube",
                        Path.GetFileNameWithoutExtension(batPath));
                }
                catch (Exception ex) { Logger.Error($"[TrayManager] {ex.GetType().Name}: {ex.Message}"); }

                WinServiceManager.Start("zapret");
                ToastNotifier.Show("Zapret",
                    $"Стратегия: {Path.GetFileNameWithoutExtension(batPath)}");
                Logger.Ok($"Tray: стратегия переключена на {Path.GetFileName(batPath)}");
            }
            else
            {
                ToastNotifier.Show("Zapret — Ошибка", "Не удалось установить службу");
            }
        }
        catch (Exception ex)
        {
            ToastNotifier.Show("Zapret — Ошибка", $"Не удалось переключить: {ex.Message}");
            Logger.Error($"Tray SwitchStrategy: {ex.Message}");
        }
    }

    private void RestartService()
    {
        try
        {
            WinServiceManager.Stop("zapret");
            Thread.Sleep(1000);
            WinServiceManager.Start("zapret");
            ToastNotifier.Show("Zapret", "Служба перезапущена");
        }
        catch (Exception ex)
        {
            ToastNotifier.Show("Zapret — Ошибка", ex.Message);
        }
    }

    private void StopService()
    {
        try
        {
            WinServiceManager.Stop("zapret");
            ProcessManager.KillAll();
            ToastNotifier.Show("Zapret", "Служба остановлена");
        }
        catch (Exception ex)
        {
            ToastNotifier.Show("Zapret — Ошибка", ex.Message);
        }
    }

    // ── Console Visibility ──────────────────────────────────────────────────

    public static void HideConsole()
    {
        var hWnd = GetConsoleWindow();
        if (hWnd != IntPtr.Zero) ShowWindow(hWnd, SW_HIDE);
    }

    public static void ShowConsole()
    {
        var hWnd = GetConsoleWindow();
        if (hWnd != IntPtr.Zero) ShowWindow(hWnd, SW_SHOW);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private string GetCurrentStrategy()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"System\CurrentControlSet\Services\zapret");
            var val = key?.GetValue("zapret-discord-youtube")?.ToString();
            return string.IsNullOrEmpty(val) ? "не установлена" : val;
        }
        catch (Exception ex) { Logger.Error($"[TrayManager] {ex.GetType().Name}: {ex.Message}"); return "?"; }
    }

    /// <summary>Create a simple colored square icon for the tray.</summary>
    private static Icon CreateStatusIcon(ConsoleColor color)
    {
        var sysColor = color switch
        {
            ConsoleColor.Green => Color.LimeGreen,
            ConsoleColor.Yellow => Color.Gold,
            ConsoleColor.Red => Color.OrangeRed,
            _ => Color.DimGray
        };

        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);

        // Draw a shield-like shape
        using var brush = new SolidBrush(sysColor);
        using var pen = new Pen(Color.FromArgb(80, 0, 0, 0), 1);
        var points = new[]
        {
            new Point(8, 1),
            new Point(14, 3),
            new Point(14, 9),
            new Point(8, 14),
            new Point(2, 9),
            new Point(2, 3)
        };
        g.FillPolygon(brush, points);
        g.DrawPolygon(pen, points);

        // "Z" letter
        using var font = new Font("Consolas", 6, FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.White);
        g.DrawString("Z", font, textBrush, 4, 3);

        return Icon.FromHandle(bmp.GetHicon());
    }
}
