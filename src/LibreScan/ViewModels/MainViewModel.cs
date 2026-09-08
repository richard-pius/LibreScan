using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using LibreScan.Helpers;
using LibreScan.Models;
using LibreScan.Services;

namespace LibreScan.ViewModels;

/// <summary>
/// Main MVVM ViewModel driving the dashboard, scan, update, and quarantine UI.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ClamAVService _clam = new();
    private CancellationTokenSource? _cts;

    // ═════════════════════════════════════════════════════════════════════════
    //  Observable Properties
    // ═════════════════════════════════════════════════════════════════════════

    private string _statusText = "Initializing Engine...";
    public string StatusText
    {
        get => _statusText;
        set => Set(ref _statusText, value);
    }

    private string _statusSubtext = "Checking system status...";
    public string StatusSubtext
    {
        get => _statusSubtext;
        set => Set(ref _statusSubtext, value);
    }

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        set { Set(ref _isScanning, value); Notify(nameof(IsBusy)); Notify(nameof(IsIdle)); }
    }

    private bool _isUpdating;
    public bool IsUpdating
    {
        get => _isUpdating;
        set { Set(ref _isUpdating, value); Notify(nameof(IsBusy)); Notify(nameof(IsIdle)); }
    }

    public bool IsBusy => IsScanning || IsUpdating;
    public bool IsIdle => !IsBusy;

    private int _threatsFoundCount;
    public int ThreatsFoundCount
    {
        get => _threatsFoundCount;
        set { Set(ref _threatsFoundCount, value); HasThreats = value > 0; }
    }

    private bool _hasThreats;
    public bool HasThreats
    {
        get => _hasThreats;
        set => Set(ref _hasThreats, value);
    }

    private int _filesScannedCount;
    public int FilesScannedCount
    {
        get => _filesScannedCount;
        set => Set(ref _filesScannedCount, value);
    }

    private string _elapsedTime = "00:00";
    public string ElapsedTime
    {
        get => _elapsedTime;
        set => Set(ref _elapsedTime, value);
    }

    private string _databaseDate = "Unknown";
    public string DatabaseDate
    {
        get => _databaseDate;
        set => Set(ref _databaseDate, value);
    }

    private StatusLevel _currentStatus = StatusLevel.Initializing;
    public StatusLevel CurrentStatus
    {
        get => _currentStatus;
        set => Set(ref _currentStatus, value);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Collections
    // ═════════════════════════════════════════════════════════════════════════

    public ObservableCollection<string>          LogEntries        { get; } = [];
    public ObservableCollection<ThreatInfo>      DetectedThreats   { get; } = [];
    public ObservableCollection<QuarantineEntry> QuarantineEntries { get; } = [];

    // ═════════════════════════════════════════════════════════════════════════
    //  Commands
    // ═════════════════════════════════════════════════════════════════════════

    public ICommand QuickScanCommand          { get; }
    public ICommand FullScanCommand           { get; }
    public ICommand UpdateCommand             { get; }
    public ICommand CancelCommand             { get; }
    public ICommand QuarantineAllCommand      { get; }
    public ICommand RestoreCommand            { get; }
    public ICommand DeleteCommand             { get; }
    public ICommand RefreshQuarantineCommand  { get; }

    // ═════════════════════════════════════════════════════════════════════════
    //  Constructor
    // ═════════════════════════════════════════════════════════════════════════

    public MainViewModel()
    {
        QuickScanCommand         = new RelayCommand(async _ => await RunQuickScanAsync(),  _ => IsIdle);
        FullScanCommand          = new RelayCommand(async _ => await RunFullScanAsync(),   _ => IsIdle);
        UpdateCommand            = new RelayCommand(async _ => await RunUpdateAsync(),     _ => IsIdle);
        CancelCommand            = new RelayCommand(_ => CancelOperation(),               _ => IsBusy);
        QuarantineAllCommand     = new RelayCommand(async _ => await QuarantineAllAsync(), _ => HasThreats);
        RestoreCommand           = new RelayCommand(async p => await RestoreAsync(p as QuarantineEntry));
        DeleteCommand            = new RelayCommand(async p => await DeleteAsync(p as QuarantineEntry));
        RefreshQuarantineCommand = new RelayCommand(async _ => await LoadQuarantineAsync());

        // Wire engine events to UI collections
        _clam.OutputReceived += (_, line) => Log(line);
        _clam.ThreatDetected += (_, threat) =>
        {
            DetectedThreats.Add(threat);
            ThreatsFoundCount = DetectedThreats.Count;
        };
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Initialization  (called once from MainWindow.Loaded)
    // ═════════════════════════════════════════════════════════════════════════

    public async Task InitializeAsync()
    {
        if (!ClamAVService.IsEngineAvailable())
        {
            StatusText    = "Engine Not Found";
            StatusSubtext = "ClamAV binaries missing from clamav_bin/";
            CurrentStatus = StatusLevel.Error;
            Log("ERROR: clamscan.exe not found. Run build_pipeline.ps1 first.");
            return;
        }

        RefreshDatabaseDate();

        // Auto-update if database is missing or stale (> 24 h)
        if (ClamAVService.IsUpdateNeeded())
        {
            Log("Virus definitions are missing or outdated (> 24 h). Starting update...");
            await RunUpdateAsync();
        }
        else
        {
            StatusText    = "System Protected";
            StatusSubtext = $"Definitions up to date · Last updated {DatabaseDate}";
            CurrentStatus = StatusLevel.Protected;
            Log("Engine ready. Virus definitions are current.");
        }

        await LoadQuarantineAsync();
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Scan
    // ═════════════════════════════════════════════════════════════════════════

    private async Task RunQuickScanAsync()
    {
        if (ClamAVService.GetDatabaseDate() is null)
        {
            Log("Virus definitions are not installed yet. Starting initial update...");
            await RunUpdateAsync();
            if (ClamAVService.GetDatabaseDate() is null)
            {
                Log("Cannot scan: virus definitions could not be downloaded.");
                return;
            }
        }

        var targets = ClamAVService.GetQuickScanTargets();
        if (targets.Count == 0)
        {
            Log("No user directories found for Quick Scan.");
            return;
        }
        await RunScanCoreAsync(targets, "Quick Scan");
    }

    private async Task RunFullScanAsync()
    {
        if (ClamAVService.GetDatabaseDate() is null)
        {
            Log("Virus definitions are not installed yet. Starting initial update...");
            await RunUpdateAsync();
            if (ClamAVService.GetDatabaseDate() is null)
            {
                Log("Cannot scan: virus definitions could not be downloaded.");
                return;
            }
        }

        var targets = ClamAVService.GetFullScanTargets();
        if (targets.Count == 0)
        {
            Log("No fixed drives found for Full Scan.");
            return;
        }
        await RunScanCoreAsync(targets, "Full Scan");
    }

    private async Task RunScanCoreAsync(IReadOnlyList<string> targets, string label)
    {
        IsScanning       = true;
        DetectedThreats.Clear();
        ThreatsFoundCount = 0;
        FilesScannedCount = 0;
        ElapsedTime       = "00:00";
        _cts              = new CancellationTokenSource();

        StatusText    = $"Running {label}...";
        StatusSubtext = "Analyzing files for threats";
        CurrentStatus = StatusLevel.Scanning;
        Log($"── {label} started ──");

        // Live elapsed-time ticker
        var sw = Stopwatch.StartNew();
        _ = TickElapsedAsync(sw, _cts.Token);

        try
        {
            var result = await _clam.RunScanAsync(targets, _cts.Token);
            sw.Stop();
            ElapsedTime       = result.Duration.ToString(@"mm\:ss");
            FilesScannedCount = result.FilesScanned;

            if (result.Cancelled)
            {
                StatusText    = "Scan Cancelled";
                StatusSubtext = "Stopped by user";
                CurrentStatus = StatusLevel.Warning;
                Log("Scan cancelled.");
            }
            else if (result.MalwareDetected)
            {
                var s = result.ThreatsFound != 1 ? "s" : "";
                StatusText    = $"{result.ThreatsFound} Threat{s} Found";
                StatusSubtext = "Review detected threats and take action";
                CurrentStatus = StatusLevel.Danger;
                Log($"{label} complete — {result.ThreatsFound} threat{s} in {result.FilesScanned} files.");
            }
            else if (result.Success)
            {
                StatusText    = "System Clean";
                StatusSubtext = $"No threats found · {result.FilesScanned} files scanned";
                CurrentStatus = StatusLevel.Protected;
                Log($"{label} complete — no threats in {result.FilesScanned} files.");
            }
            else
            {
                StatusText    = "Scan Error";
                StatusSubtext = $"ClamAV exit code {result.ExitCode}";
                CurrentStatus = StatusLevel.Error;
                Log($"Scan error (exit code {result.ExitCode}).");
            }
        }
        catch (Exception ex)
        {
            StatusText    = "Scan Error";
            StatusSubtext = ex.Message;
            CurrentStatus = StatusLevel.Error;
            Log($"Scan error: {ex.Message}");
        }
        finally
        {
            _cts?.Cancel();          // stop the ticker
            _cts?.Dispose();
            _cts       = null;
            IsScanning = false;
        }
    }

    private async Task TickElapsedAsync(Stopwatch sw, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(1000, ct);
                Application.Current?.Dispatcher.InvokeAsync(() =>
                    ElapsedTime = sw.Elapsed.ToString(@"mm\:ss"));
            }
        }
        catch (OperationCanceledException) { /* expected */ }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Update Definitions
    // ═════════════════════════════════════════════════════════════════════════

    private async Task RunUpdateAsync()
    {
        IsUpdating = true;
        _cts       = new CancellationTokenSource();

        StatusText    = "Updating Definitions...";
        StatusSubtext = "Downloading latest virus signatures";
        CurrentStatus = StatusLevel.Updating;
        Log("Starting virus definition update...");

        try
        {
            bool ok = await _clam.UpdateDefinitionsAsync(_cts.Token);
            RefreshDatabaseDate();

            if (ok)
            {
                StatusText    = "System Protected";
                StatusSubtext = $"Definitions updated · {DatabaseDate}";
                CurrentStatus = StatusLevel.Protected;
                Log("Virus definitions updated successfully.");
            }
            else
            {
                StatusText    = "Update Failed";
                StatusSubtext = "Could not download virus definitions";
                CurrentStatus = StatusLevel.Warning;
                Log("Virus definition update failed.");
            }
        }
        catch (Exception ex)
        {
            StatusText    = "Update Error";
            StatusSubtext = ex.Message;
            CurrentStatus = StatusLevel.Error;
            Log($"Update error: {ex.Message}");
        }
        finally
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts       = null;
            IsUpdating = false;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Cancel
    // ═════════════════════════════════════════════════════════════════════════

    private void CancelOperation()
    {
        _cts?.Cancel();
        _clam.KillCurrentProcess();
        Log("Operation cancelled by user.");
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Quarantine
    // ═════════════════════════════════════════════════════════════════════════

    private async Task QuarantineAllAsync()
    {
        int count = 0;
        foreach (var t in DetectedThreats.ToList())
        {
            if (await _clam.QuarantineFileAsync(t))
            {
                Log($"Quarantined: {t.FilePath}");
                count++;
            }
            else
            {
                Log($"Failed to quarantine: {t.FilePath}");
            }
        }
        DetectedThreats.Clear();
        ThreatsFoundCount = 0;
        await LoadQuarantineAsync();
        Log($"{count} file(s) moved to quarantine.");

        StatusText    = "Threats Quarantined";
        StatusSubtext = $"{count} file(s) isolated in secure quarantine";
        CurrentStatus = StatusLevel.Protected;
    }

    private async Task LoadQuarantineAsync()
    {
        var entries = await _clam.GetQuarantineEntriesAsync();
        QuarantineEntries.Clear();
        foreach (var e in entries) QuarantineEntries.Add(e);
    }

    private async Task RestoreAsync(QuarantineEntry? entry)
    {
        if (entry is null) return;
        if (await _clam.RestoreFromQuarantineAsync(entry))
        {
            Log($"Restored: {entry.OriginalPath}");
            await LoadQuarantineAsync();
        }
    }

    private async Task DeleteAsync(QuarantineEntry? entry)
    {
        if (entry is null) return;
        if (await _clam.DeleteFromQuarantineAsync(entry))
        {
            Log($"Permanently deleted: {entry.OriginalFileName}");
            await LoadQuarantineAsync();
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════════════════

    private void RefreshDatabaseDate()
    {
        var d = ClamAVService.GetDatabaseDate();
        DatabaseDate = d?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "Not downloaded";
    }

    private void Log(string message)
    {
        void Append()
        {
            var ts = DateTime.Now.ToString("HH:mm:ss");
            LogEntries.Add($"[{ts}] {message}");

            // Prevent unbounded memory growth
            while (LogEntries.Count > 2000)
                LogEntries.RemoveAt(0);
        }

        if (Application.Current?.Dispatcher is { } d && !d.CheckAccess())
        {
            d.InvokeAsync(Append);
        }
        else
        {
            Append();
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  INotifyPropertyChanged
    // ═════════════════════════════════════════════════════════════════════════

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Notify([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Notify(name);
        return true;
    }
}

// ─────────────────────────────────────────────────────────────────────────────

public enum StatusLevel
{
    Initializing,
    Protected,
    Scanning,
    Updating,
    Warning,
    Danger,
    Error,
}
