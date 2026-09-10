using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using LibreScan.Helpers;

// Disambiguate WPF Application from WinForms Application
using Application = System.Windows.Application;

namespace LibreScan;

/// <summary>
/// Application lifecycle manager.
/// Enforces single-instance execution, handles --startup silent boot, and manages tray icon lifecycle.
/// </summary>
public partial class App : Application
{
    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegisterWindowMessage(string lpString);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, ref COPYDATASTRUCT lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    public const int WM_COPYDATA = 0x004A;
    public static readonly int WM_SHOWME = RegisterWindowMessage("LibreScan_ShowMainWindow_SingleInstance");
    private const int HWND_BROADCAST = 0xffff;

    private static Mutex? _singleInstanceMutex;
    private static bool _ownsMutex;
    private TrayIconManager? _trayManager;

    public static void SignalExistingInstance()
    {
        PostMessage((IntPtr)HWND_BROADCAST, WM_SHOWME, IntPtr.Zero, IntPtr.Zero);
    }

    public static bool SendPathToExistingInstance(string targetPath)
    {
        IntPtr hWnd = FindLibreScanWindow();
        if (hWnd == IntPtr.Zero)
        {
            SignalExistingInstance();
            return false;
        }

        IntPtr buffer = Marshal.StringToHGlobalUni(targetPath);
        try
        {
            var cds = new COPYDATASTRUCT
            {
                dwData = IntPtr.Zero,
                cbData = (targetPath.Length + 1) * sizeof(char),
                lpData = buffer
            };

            SendMessage(hWnd, WM_COPYDATA, IntPtr.Zero, ref cds);
            return true;
        }
        catch
        {
            SignalExistingInstance();
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IntPtr FindLibreScanWindow()
    {
        IntPtr hWnd = FindWindow(null, "LibreScan Security");
        if (hWnd != IntPtr.Zero) return hWnd;

        try
        {
            var current = System.Diagnostics.Process.GetCurrentProcess();
            var processes = System.Diagnostics.Process.GetProcessesByName(current.ProcessName);
            foreach (var p in processes)
            {
                if (p.Id != current.Id && p.MainWindowHandle != IntPtr.Zero)
                {
                    return p.MainWindowHandle;
                }
            }
        }
        catch { }

        return IntPtr.Zero;
    }

    public static string? GetTargetPath(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i].Trim('\"', ' ');
            if (string.Equals(arg, "--startup", StringComparison.OrdinalIgnoreCase))
                continue;

            if (arg.StartsWith("/scan:", StringComparison.OrdinalIgnoreCase))
            {
                string path = arg.Substring(6).Trim('\"', ' ');
                if (!string.IsNullOrEmpty(path))
                    return path;
            }
            else if (string.Equals(arg, "/scan", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "-scan", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    string path = args[i + 1].Trim('\"', ' ');
                    if (!string.IsNullOrEmpty(path))
                        return path;
                }
            }
            else if (!arg.StartsWith('-') && !arg.StartsWith('/'))
            {
                if (!string.IsNullOrWhiteSpace(arg))
                    return arg;
            }
        }
        return null;
    }

    private void OnStartup(object sender, StartupEventArgs e)
    {
        string? targetPath = GetTargetPath(e.Args);

        // ── Single-Instance Enforcement ──────────────────────────────────
        bool createdNew = false;
        try
        {
            _singleInstanceMutex = new Mutex(true, "Local\\LibreScanSecurity_SingleInstanceMutex", out createdNew);
            _ownsMutex = createdNew;
        }
        catch (AbandonedMutexException)
        {
            // The mutex was abandoned by a terminated process; we now own it.
            createdNew = true;
            _ownsMutex = true;
        }

        if (!createdNew)
        {
            if (!string.IsNullOrEmpty(targetPath))
            {
                SendPathToExistingInstance(targetPath);
            }
            else
            {
                SignalExistingInstance();
            }
            Shutdown();
            return;
        }

        // ── Silent Boot ──────────────────────────────────────────────────
        bool silentBoot = false;
        foreach (string arg in e.Args)
        {
            if (arg.Equals("--startup", StringComparison.OrdinalIgnoreCase))
            {
                silentBoot = true;
                break;
            }
        }

        // Global crash logging guards (writes to user LocalAppData for non-admin permission safety)
        static void LogCrash(string source, object? ex)
        {
            try
            {
                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LibreScan");
                Directory.CreateDirectory(appData);
                string logPath = Path.Combine(appData, "crash.log");
                File.AppendAllText(logPath, $"[{DateTime.UtcNow:u}] [{source}] {ex}\r\n");
            }
            catch { }
        }

        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash("Dispatcher", args.Exception);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            LogCrash("AppDomain", args.ExceptionObject);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogCrash("TaskScheduler", args.Exception);
            args.SetObserved();
        };

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;

        _trayManager = new TrayIconManager(mainWindow);
        _trayManager.Initialize();
        mainWindow.SetTrayManager(_trayManager);

        _trayManager.ExitRequested += (_, _) =>
        {
            mainWindow.Shutdown();
            _trayManager?.Dispose();
            Shutdown();
        };

        if (!silentBoot || !string.IsNullOrEmpty(targetPath))
        {
            mainWindow.Show();
        }

        if (!string.IsNullOrEmpty(targetPath))
        {
            mainWindow.ActivateFromTray();
            Dispatcher.InvokeAsync(async () => await mainWindow.ScanExternalTargetAsync(targetPath));
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);
        if (MainWindow is MainWindow mw)
            mw.Shutdown();
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        if (MainWindow is MainWindow mw)
            mw.Shutdown();

        _trayManager?.Dispose();

        if (_ownsMutex && _singleInstanceMutex is not null)
        {
            try { _singleInstanceMutex.ReleaseMutex(); } catch { }
        }
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
    }
}
