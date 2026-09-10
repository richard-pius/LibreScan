using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using LibreScan.Services;
using WinForms = System.Windows.Forms;

namespace LibreScan.Helpers;

/// <summary>
/// Manages the System Tray (NotifyIcon) lifecycle.
/// Disposes unmanaged GDI handles and NotifyIcon to prevent taskbar ghosting.
/// </summary>
public sealed partial class TrayIconManager : IDisposable
{
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr hIcon);

    private WinForms.NotifyIcon?       _notifyIcon;
    private WinForms.ContextMenuStrip? _contextMenu;
    private Icon?                      _trayIcon;
    private readonly Window            _mainWindow;

    public event EventHandler? QuickScanRequested;
    public event EventHandler? UpdateRequested;
    public event EventHandler? ExitRequested;

    public TrayIconManager(Window mainWindow)
    {
        _mainWindow = mainWindow;
    }

    public void Initialize()
    {
        // ── Context Menu ─────────────────────────────────────────────────
        _contextMenu = new WinForms.ContextMenuStrip();

        var openItem = _contextMenu.Items.Add("Open LibreScan Security");
        openItem.Click += (_, _) => ShowMainWindow();
        openItem.Font  = new Font(openItem.Font!, System.Drawing.FontStyle.Bold);

        _contextMenu.Items.Add(new WinForms.ToolStripSeparator());

        var quickItem = _contextMenu.Items.Add("Quick Scan");
        quickItem.Click += (_, _) =>
        {
            ShowMainWindow();
            QuickScanRequested?.Invoke(this, EventArgs.Empty);
        };

        var updateItem = _contextMenu.Items.Add("Update Definitions");
        updateItem.Click += (_, _) =>
        {
            ShowMainWindow();
            UpdateRequested?.Invoke(this, EventArgs.Empty);
        };

        _contextMenu.Items.Add(new WinForms.ToolStripSeparator());

        var exitItem = _contextMenu.Items.Add("Exit");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        // ── NotifyIcon ───────────────────────────────────────────────────
        _trayIcon = CreateShieldIcon();

        _notifyIcon = new WinForms.NotifyIcon
        {
            Text             = "LibreScan Security",
            Icon             = _trayIcon,
            Visible          = true,
            ContextMenuStrip = _contextMenu,
        };

        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
        _notifyIcon.BalloonTipClicked += (_, _) => ShowMainWindow();
    }

    /// <summary>Show a balloon tooltip from the tray icon.</summary>
    public void ShowBalloon(string title, string text,
        WinForms.ToolTipIcon icon = WinForms.ToolTipIcon.Info)
    {
        try
        {
            _notifyIcon?.ShowBalloonTip(4000, title, text, icon);
        }
        catch { }
    }

    /// <summary>Displays notification based on scan outcome.</summary>
    public void NotifyScanCompleted(Models.ScanResult result, string label)
    {
        if (result.Cancelled) return;

        if (result.MalwareDetected || result.ThreatsFound > 0)
        {
            ShowBalloon(
                "⚠ Threats Detected!",
                $"{result.ThreatsFound} threat(s) found during {label}. Click to review and quarantine.",
                WinForms.ToolTipIcon.Warning);
        }
        else if (result.Success)
        {
            ShowBalloon(
                "Scan Complete",
                $"{label} finished. No threats found in {result.FilesScanned:N0} files.",
                WinForms.ToolTipIcon.Info);
        }
        else
        {
            ShowBalloon(
                "Scan Finished",
                $"{label} encountered warnings or locked files. Exit code: {result.ExitCode}.",
                WinForms.ToolTipIcon.Warning);
        }
    }

    /// <summary>Displays notification based on update outcome.</summary>
    public void NotifyUpdateCompleted(bool success)
    {
        if (success)
        {
            ShowBalloon(
                "Definitions Updated",
                "ClamAV virus definitions have been successfully updated.",
                WinForms.ToolTipIcon.Info);
        }
        else
        {
            ShowBalloon(
                "Update Notice",
                "Could not refresh virus definitions. Will retry automatically.",
                WinForms.ToolTipIcon.Warning);
        }
    }

    /// <summary>Update the tooltip text shown on hover.</summary>
    public void UpdateTooltip(string? text)
    {
        if (_notifyIcon is not null)
        {
            string s = string.IsNullOrWhiteSpace(text) ? "LibreScan Security" : text;
            _notifyIcon.Text = s.Length > 63 ? s[..63] : s;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    public void ShowMainWindow()
    {
        if (_mainWindow is MainWindow mw)
        {
            mw.ActivateFromTray();
        }
        else
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }
    }

    /// <summary>
    /// Generates a teal shield + checkmark icon programmatically or loads librescan.ico.
    /// Safely frees unmanaged GDI handles with DestroyIcon.
    /// </summary>
    private static Icon CreateShieldIcon()
    {
        try
        {
            string[] candidatePaths = [
                Path.Combine(AppContext.BaseDirectory, "Assets", "librescan.ico"),
                Path.Combine(ClamAVService.BaseDir, "Assets", "librescan.ico"),
            ];

            foreach (var p in candidatePaths)
            {
                if (File.Exists(p))
                {
                    return new Icon(p, 32, 32);
                }
            }

            using var bitmap = new Bitmap(32, 32,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bitmap);
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.Clear(Color.Transparent);

            // Shield body
            using var shieldPath = new GraphicsPath();
            shieldPath.AddLines([
                new PointF(16, 1), new PointF(29, 7), new PointF(29, 17),
                new PointF(16, 30), new PointF(3, 17), new PointF(3, 7),
            ]);
            shieldPath.CloseFigure();

            using var brush = new LinearGradientBrush(
                new Rectangle(0, 0, 32, 32),
                Color.FromArgb(45, 212, 191),     // teal-400
                Color.FromArgb(16, 185, 129),     // emerald-500
                LinearGradientMode.Vertical);
            g.FillPath(brush, shieldPath);

            // Checkmark
            using var pen = new Pen(Color.White, 2.5f)
            {
                StartCap = LineCap.Round,
                EndCap   = LineCap.Round,
                LineJoin = LineJoin.Round,
            };
            g.DrawLines(pen, [
                new PointF(10, 16), new PointF(14, 21), new PointF(22, 11),
            ]);

            var hIcon = bitmap.GetHicon();
            try
            {
                using var tempIcon = Icon.FromHandle(hIcon);
                return (Icon)tempIcon.Clone();
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }
        catch
        {
            return SystemIcons.Shield;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _contextMenu?.Dispose();
        _contextMenu = null;

        _trayIcon?.Dispose();
        _trayIcon = null;
    }
}
