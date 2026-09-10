using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using LibreScan.Helpers;
using LibreScan.ViewModels;

namespace LibreScan;

/// <summary>
/// Main window code-behind.
/// Handles dark title bar, view switching, tray minimize, single-instance activation, and log auto-scroll.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    // ── Dark title bar & UIPI P/Invoke ──────────────────────────────────────
    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(
        IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ChangeWindowMessageFilter(int message, int dwFlag);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int MSGFLT_ADD = 1;
    public const int WM_COPYDATA = 0x004A;

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    // ─────────────────────────────────────────────────────────────────────────

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        Loaded       += OnLoaded;
        StateChanged += OnStateChanged;

        // Auto-scroll the log listbox as new entries arrive
        _vm.LogEntries.CollectionChanged += OnLogEntriesChanged;
    }

    /// <summary>Enable immersive dark title bar and register single-instance window hook.</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        int darkMode = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE,
            ref darkMode, sizeof(int));

        // Allow WM_SHOWME and WM_COPYDATA across User Interface Privilege Isolation (UIPI) boundaries
        ChangeWindowMessageFilter(App.WM_SHOWME, MSGFLT_ADD);
        ChangeWindowMessageFilter(WM_COPYDATA, MSGFLT_ADD);

        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == App.WM_SHOWME)
        {
            ActivateFromTray();
            handled = true;
            return (IntPtr)1;
        }

        if (msg == WM_COPYDATA && lParam != IntPtr.Zero)
        {
            try
            {
                var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
                if (cds.lpData != IntPtr.Zero)
                {
                    string target = Marshal.PtrToStringUni(cds.lpData) ?? string.Empty;
                    ActivateFromTray();
                    if (NavDashboard is not null)
                        NavDashboard.IsChecked = true;
                    SwitchView(DashboardPanel);

                    if (!string.IsNullOrWhiteSpace(target))
                    {
                        Dispatcher.InvokeAsync(async () => await _vm.ScanPathFromExternalAsync(target));
                    }
                    handled = true;
                    return (IntPtr)1;
                }
            }
            catch { }
        }

        return IntPtr.Zero;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _vm.InitializeAsync();
    }

    // ── Drag & Drop Scanning ─────────────────────────────────────────────────

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            if (DragDropOverlay is not null)
                DragDropOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        if (DragDropOverlay is not null)
            DragDropOverlay.Visibility = Visibility.Collapsed;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (DragDropOverlay is not null)
            DragDropOverlay.Visibility = Visibility.Collapsed;

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
            {
                if (NavDashboard is not null)
                    NavDashboard.IsChecked = true;
                SwitchView(DashboardPanel);
                await _vm.HandleDroppedPathsAsync(paths);
            }
        }
        e.Handled = true;
    }

    // ── Minimize to tray instead of closing ──────────────────────────────────

    public bool IsExiting { get; set; }
    private bool _isShuttingDown;

    public void Shutdown()
    {
        if (_isShuttingDown) return;
        _isShuttingDown = true;
        IsExiting = true;
        _vm.Shutdown();
    }

    public async Task ScanExternalTargetAsync(string target)
    {
        await _vm.ScanPathFromExternalAsync(target);
    }

    public void ActivateFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Topmost = true;
        Activate();
        Topmost = false;
        Focus();

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            try { SetForegroundWindow(hwnd); } catch { }
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (IsExiting)
        {
            Shutdown();
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;        // Prevent close — hide to tray instead
        Hide();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
            Hide();
    }

    // ── Tray integration (called from App.xaml.cs) ───────────────────────────

    public void SetTrayManager(TrayIconManager tray)
    {
        tray.QuickScanRequested += (_, _) =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (_vm.QuickScanCommand.CanExecute(null))
                    _vm.QuickScanCommand.Execute(null);
            });
        };

        tray.UpdateRequested += (_, _) =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (_vm.UpdateCommand.CanExecute(null))
                    _vm.UpdateCommand.Execute(null);
            });
        };

        _vm.ScanCompleted += (result, label) => tray.NotifyScanCompleted(result, label);
        _vm.UpdateCompleted += ok => tray.NotifyUpdateCompleted(ok);
        _vm.StatusChanged += status => tray.UpdateTooltip($"LibreScan Security — {status}");
    }

    // ── View switching (sidebar nav) ─────────────────────────────────────────

    private void NavDashboard_Checked(object sender, RoutedEventArgs e)
        => SwitchView(DashboardPanel);

    private void NavQuarantine_Checked(object sender, RoutedEventArgs e)
    {
        SwitchView(QuarantinePanel);
        _vm?.RefreshQuarantineCommand?.Execute(null);
    }

    private void GoToQuarantine_Click(object sender, RoutedEventArgs e)
    {
        if (NavQuarantine is not null)
            NavQuarantine.IsChecked = true;
    }

    private void SwitchView(UIElement? active)
    {
        if (DashboardPanel is null || QuarantinePanel is null)
            return;

        DashboardPanel.Visibility  = Visibility.Collapsed;
        QuarantinePanel.Visibility = Visibility.Collapsed;

        if (active is not null)
            active.Visibility = Visibility.Visible;
    }

    // ── Auto-scroll log (debounced to avoid UI queue flooding) ──────────────

    private bool _scrollRequested;

    private void OnLogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && LogListBox.Items.Count > 0)
        {
            if (_scrollRequested) return;
            _scrollRequested = true;

            Dispatcher.InvokeAsync(() =>
            {
                _scrollRequested = false;
                if (LogListBox.Items.Count > 0)
                {
                    try
                    {
                        LogListBox.ScrollIntoView(LogListBox.Items[^1]);
                    }
                    catch
                    {
                        // Defensive against layout re-entrancy
                    }
                }
            }, System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}
