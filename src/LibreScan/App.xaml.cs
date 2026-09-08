using System.Windows;
using LibreScan.Helpers;

// Disambiguate WPF Application from WinForms Application
using Application = System.Windows.Application;

namespace LibreScan;

/// <summary>
/// Application lifecycle manager.
/// Handles --startup silent boot and tray icon disposal.
/// </summary>
public partial class App : Application
{
    private TrayIconManager? _trayManager;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        // ── Silent Boot (CRITICAL) ───────────────────────────────────────
        // When launched via the registry Run key with --startup,
        // initialize the tray icon but do NOT show the main window.
        bool silentBoot = false;
        foreach (string arg in e.Args)
        {
            if (arg.Equals("--startup", StringComparison.OrdinalIgnoreCase))
            {
                silentBoot = true;
                break;
            }
        }

        // Global crash guard to prevent silent process termination
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
        };

        // Use explicit shutdown mode so closing the window hides to tray
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;

        // ── Tray Lifecycle ───────────────────────────────────────────────
        // Initialize ALWAYS (regardless of boot mode) so the user
        // can interact via the tray icon even in silent boot.
        _trayManager = new TrayIconManager(mainWindow);
        _trayManager.Initialize();
        // Wire tray menu actions to the window's ViewModel
        mainWindow.SetTrayManager(_trayManager);

        _trayManager.ExitRequested += (_, _) =>
        {
            mainWindow.IsExiting = true;
            _trayManager?.Dispose();
            Shutdown();
        };

        if (!silentBoot)
        {
            mainWindow.Show();
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);
        if (MainWindow is MainWindow mw)
            mw.IsExiting = true;
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        // ── CRITICAL: Dispose NotifyIcon to prevent taskbar ghosting ─────
        _trayManager?.Dispose();
    }
}
