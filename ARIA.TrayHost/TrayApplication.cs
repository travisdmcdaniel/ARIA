using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
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

    private SettingsWindow? _settingsWindow;

    public TrayApplication()
    {
        _iconGreen = CreateColorIcon(Color.FromArgb(34, 197, 94));
        _iconAmber = CreateColorIcon(Color.FromArgb(234, 179, 8));
        _iconRed   = CreateColorIcon(Color.FromArgb(239, 68, 68));

        _tray = new NotifyIcon
        {
            Icon    = _iconGreen,
            Text    = "ARIA — Running",
            Visible = true,
            ContextMenuStrip = BuildContextMenu()
        };

        _tray.DoubleClick += (_, _) => OpenSettings();
    }

    // ── Context menu ──────────────────────────────────────────────────────────

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        var statusItem = new ToolStripMenuItem("Status: Running") { Enabled = false };
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings",      null, (_, _) => OpenSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open Logs Folder", null, (_, _) => OpenLogsFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit",          null, (_, _) => ExitApplication());

        return menu;
    }

    // ── Actions ───────────────────────────────────────────────────────────────

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

    // ── Status update (called by IPC client) ──────────────────────────────────

    public void SetStatus(AgentStatus status, string tooltip)
    {
        _tray.Icon = status switch
        {
            AgentStatus.Running  => _iconGreen,
            AgentStatus.Degraded => _iconAmber,
            _                    => _iconRed
        };
        _tray.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip; // NotifyIcon max is 63
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
