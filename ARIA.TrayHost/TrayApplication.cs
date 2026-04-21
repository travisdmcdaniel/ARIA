using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.ServiceProcess;
using ARIA.Core.Constants;
using ARIA.TrayHost.Settings;

namespace ARIA.TrayHost;

/// <summary>
/// WinForms ApplicationContext that owns the system-tray NotifyIcon.
/// The WPF SettingsWindow is spawned on demand on this STA thread.
/// </summary>
public sealed class TrayApplication : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly Icon _iconGreen;
    private readonly Icon _iconAmber;
    private readonly Icon _iconRed;

    private ToolStripMenuItem _statusItem = null!;
    private ToolStripMenuItem _pauseItem = null!;
    private SettingsWindow? _settingsWindow;
    private bool _isPaused;

    public TrayApplication()
    {
        _iconGreen = CreateColorIcon(Color.FromArgb(34, 197, 94));
        _iconAmber = CreateColorIcon(Color.FromArgb(234, 179, 8));
        _iconRed   = CreateColorIcon(Color.FromArgb(239, 68, 68));

        _isPaused = PauseFlag.IsSet;

        _tray = new NotifyIcon
        {
            Icon    = _isPaused ? _iconAmber : _iconGreen,
            Text    = _isPaused ? "ARIA — Paused" : "ARIA — Running",
            Visible = true,
            ContextMenuStrip = BuildContextMenu()
        };

        _tray.DoubleClick += (_, _) => OpenSettings();
    }

    // ── Context menu ──────────────────────────────────────────────────────────

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        _statusItem = new ToolStripMenuItem(_isPaused ? "Status: Paused" : "Status: Running")
        {
            Enabled = false
        };

        _pauseItem = new ToolStripMenuItem(
            _isPaused ? "Enable" : "Disable",
            null,
            (_, _) => TogglePause());

        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_pauseItem);
        menu.Items.Add("Restart", null, (_, _) => RestartService());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings",         null, (_, _) => OpenSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open Logs Folder", null, (_, _) => OpenLogsFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit",             null, (_, _) => ExitApplication());

        return menu;
    }

    // ── Pause / resume ────────────────────────────────────────────────────────

    private void TogglePause()
    {
        if (_isPaused)
        {
            PauseFlag.Clear();
            _isPaused = false;
            _pauseItem.Text  = "Disable";
            _statusItem.Text = "Status: Running";
            _tray.Icon = _iconGreen;
            _tray.Text = "ARIA — Running";
        }
        else
        {
            PauseFlag.Set();
            _isPaused = true;
            _pauseItem.Text  = "Enable";
            _statusItem.Text = "Status: Paused";
            _tray.Icon = _iconAmber;
            _tray.Text = "ARIA — Paused";
        }
    }

    // ── Service restart ───────────────────────────────────────────────────────

    private void RestartService()
    {
        try
        {
            using var sc = new ServiceController("ARIAService");
            if (sc.Status != ServiceControllerStatus.Stopped)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
            }
            sc.Start();
            _tray.ShowBalloonTip(3000, "ARIA", "Service restarted.", ToolTipIcon.Info);
        }
        catch (InvalidOperationException)
        {
            // Service not installed — likely running via dotnet run in dev mode
            _tray.ShowBalloonTip(
                4000, "ARIA",
                "ARIAService is not registered. Restart the process manually.",
                ToolTipIcon.Warning);
        }
    }

    // ── Other actions ─────────────────────────────────────────────────────────

    private void OpenSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow();
        _settingsWindow.Show();
    }

    private static void OpenLogsFolder()
    {
        var logsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ARIA", "logs");
        Directory.CreateDirectory(logsDir);
        System.Diagnostics.Process.Start("explorer.exe", logsDir);
    }

    private void ExitApplication()
    {
        _tray.Visible = false;
        _settingsWindow?.Close();
        Application.Exit();
    }

    // ── Status update (called by IPC client in M11) ───────────────────────────

    public void SetStatus(AgentStatus status, string tooltip)
    {
        if (_isPaused) return; // tray already showing amber for pause — don't override

        _tray.Icon = status switch
        {
            AgentStatus.Running  => _iconGreen,
            AgentStatus.Degraded => _iconAmber,
            _                    => _iconRed
        };
        _tray.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;
    }

    // ── Icon generation ───────────────────────────────────────────────────────

    private static Icon CreateColorIcon(Color color)
    {
        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, 1, 1, 13, 13);
        var hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _iconGreen.Dispose();
            _iconAmber.Dispose();
            _iconRed.Dispose();
        }
        base.Dispose(disposing);
    }
}

public enum AgentStatus { Running, Degraded, Stopped }
