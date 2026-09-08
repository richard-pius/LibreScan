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
/// Handles dark title bar, view switching, tray minimize, and log auto-scroll.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    // ── Dark title bar P/Invoke ──────────────────────────────────────────────
    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(
        IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

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

    /// <summary>Enable immersive dark title bar on Windows 10 1903+ / 11.</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        int darkMode = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE,
            ref darkMode, sizeof(int));
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _vm.InitializeAsync();
    }

    // ── Minimize to tray instead of closing ──────────────────────────────────

    public bool IsExiting { get; set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (IsExiting)
        {
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
            if (_vm.QuickScanCommand.CanExecute(null))
                _vm.QuickScanCommand.Execute(null);
        };

        tray.UpdateRequested += (_, _) =>
        {
            if (_vm.UpdateCommand.CanExecute(null))
                _vm.UpdateCommand.Execute(null);
        };
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

    // ── Auto-scroll log ──────────────────────────────────────────────────────

    private void OnLogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && LogListBox.Items.Count > 0)
        {
            Dispatcher.InvokeAsync(() =>
            {
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
