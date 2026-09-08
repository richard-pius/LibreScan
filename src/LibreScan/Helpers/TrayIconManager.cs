using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace LibreScan.Helpers;

/// <summary>
/// Manages the System Tray (NotifyIcon) lifecycle.
/// CRITICAL: Dispose() MUST be called in Application.Exit to prevent taskbar ghosting.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private WinForms.NotifyIcon?       _notifyIcon;
    private WinForms.ContextMenuStrip? _contextMenu;
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
        _notifyIcon = new WinForms.NotifyIcon
        {
            Text             = "LibreScan Security",
            Icon             = CreateShieldIcon(),
            Visible          = true,
            ContextMenuStrip = _contextMenu,
        };

        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    /// <summary>Show a balloon tooltip from the tray icon.</summary>
    public void ShowBalloon(string title, string text,
        WinForms.ToolTipIcon icon = WinForms.ToolTipIcon.Info)
    {
        _notifyIcon?.ShowBalloonTip(3000, title, text, icon);
    }

    /// <summary>Update the tooltip text shown on hover.</summary>
    public void UpdateTooltip(string text)
    {
        if (_notifyIcon is not null)
            _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
    }

    // ─────────────────────────────────────────────────────────────────────────

    private void ShowMainWindow()
    {
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    /// <summary>
    /// Generates a teal shield + checkmark icon programmatically so the app
    /// ships without external icon dependencies.
    /// </summary>
    private static Icon CreateShieldIcon()
    {
        try
        {
            string appIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "librescan.ico");
            if (File.Exists(appIconPath))
            {
                return new Icon(appIconPath, 32, 32);
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
            return Icon.FromHandle(hIcon);
        }
        catch
        {
            // Fallback to the built-in shield icon
            return SystemIcons.Shield;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// MUST be called in Application.Exit to prevent taskbar ghosting.
    /// </summary>
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
    }
}
