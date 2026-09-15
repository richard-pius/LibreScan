using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
    private DateTime _lastUpdateAttemptUtc = DateTime.MinValue;
    public static readonly TimeSpan MinUpdateInterval = TimeSpan.FromMinutes(15);
    private readonly HashSet<string> _activeQuarantineOperations = [];

    // ═════════════════════════════════════════════════════════════════════════
    //  Observable Properties
    // ═════════════════════════════════════════════════════════════════════════

    public event Action<ScanResult, string>? ScanCompleted;
    public event Action<bool>?               UpdateCompleted;
    public event Action<string>?             StatusChanged;

    private string _statusText = "Initializing Engine...";
    public string StatusText
    {
        get => _statusText;
        set
        {
            if (Set(ref _statusText, value))
                StatusChanged?.Invoke(value);
        }
    }

    private string _statusSubtext = "Checking system status...";
    public string StatusSubtext
    {
        get => _statusSubtext;
        set => Set(ref _statusSubtext, value);
    }

    private string _lastScanSummary = "No recent scan";
    public string LastScanSummary
    {
        get => _lastScanSummary;
        set => Set(ref _lastScanSummary, value);
    }

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        set
        {
            if (Set(ref _isScanning, value))
            {
                Notify(nameof(IsBusy));
                Notify(nameof(IsIdle));
                Notify(nameof(CanQuarantine));
                InvalidateCommands();
            }
        }
    }

    private bool _isUpdating;
    public bool IsUpdating
    {
        get => _isUpdating;
        set
        {
            if (Set(ref _isUpdating, value))
            {
                Notify(nameof(IsBusy));
                Notify(nameof(IsIdle));
                Notify(nameof(CanQuarantine));
                InvalidateCommands();
            }
        }
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
        set
        {
            if (Set(ref _hasThreats, value))
            {
                Notify(nameof(CanQuarantine));
                InvalidateCommands();
            }
        }
    }

    private bool _isQuarantining;
    public bool IsQuarantining
    {
        get => _isQuarantining;
        set
        {
            if (Set(ref _isQuarantining, value))
            {
                Notify(nameof(CanQuarantine));
                InvalidateCommands();
            }
        }
    }

    public bool CanQuarantine => HasThreats && !IsQuarantining && !IsBusy;

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

    private string _currentScanningFile = string.Empty;
    public string CurrentScanningFile
    {
        get => _currentScanningFile;
        set => Set(ref _currentScanningFile, value);
    }

    private bool _isDriveSelectorOpen;
    public bool IsDriveSelectorOpen
    {
        get => _isDriveSelectorOpen;
        set => Set(ref _isDriveSelectorOpen, value);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Collections
    // ═════════════════════════════════════════════════════════════════════════

    public ObservableCollection<string>          LogEntries        { get; } = [];
    public ObservableCollection<ThreatInfo>      DetectedThreats   { get; } = [];
    public ObservableCollection<QuarantineEntry> QuarantineEntries { get; } = [];
    public ObservableCollection<DriveItem>       AvailableDrives   { get; } = [];

    // ═════════════════════════════════════════════════════════════════════════
    //  Commands
    // ═════════════════════════════════════════════════════════════════════════

    public ICommand QuickScanCommand          { get; }
    public ICommand FullScanCommand           { get; }
    public ICommand ScanFileCommand           { get; }
    public ICommand ScanFolderCommand         { get; }
    public ICommand OpenDriveSelectorCommand  { get; }
    public ICommand CloseDriveSelectorCommand { get; }
    public ICommand ScanSpecificDriveCommand  { get; }
    public ICommand RefreshDrivesCommand      { get; }
    public ICommand UpdateCommand             { get; }
    public ICommand CancelCommand             { get; }
    public ICommand QuarantineAllCommand      { get; }
    public ICommand QuarantineSingleCommand   { get; }
    public ICommand DismissThreatCommand      { get; }
    public ICommand EmptyQuarantineCommand    { get; }
    public ICommand RestoreAllQuarantineCommand { get; }
    public ICommand ExportLogCommand          { get; }
    public ICommand ClearLogCommand           { get; }
    public ICommand RestoreCommand            { get; }
    public ICommand DeleteCommand             { get; }
    public ICommand RefreshQuarantineCommand  { get; }

    // ═════════════════════════════════════════════════════════════════════════
    //  Constructor
    // ═════════════════════════════════════════════════════════════════════════

    public MainViewModel()
    {
        QuickScanCommand            = new RelayCommand(async _ => await RunQuickScanAsync(),  _ => IsIdle);
        FullScanCommand             = new RelayCommand(async _ => await RunFullScanAsync(),   _ => IsIdle);
        ScanFileCommand             = new RelayCommand(async _ => await RunFileScanAsync(),   _ => IsIdle);
        ScanFolderCommand           = new RelayCommand(async _ => await RunFolderScanAsync(), _ => IsIdle);
        OpenDriveSelectorCommand    = new RelayCommand(_ => OpenDriveSelector(),              _ => IsIdle);
        CloseDriveSelectorCommand   = new RelayCommand(_ => IsDriveSelectorOpen = false);
        ScanSpecificDriveCommand    = new RelayCommand(async p => await RunSpecificDriveScanAsync(p as DriveItem), _ => IsIdle);
        RefreshDrivesCommand        = new RelayCommand(_ => RefreshDrives(),                  _ => IsIdle);
        UpdateCommand               = new RelayCommand(async _ => await RunUpdateAsync(force: false), _ => IsIdle);
        CancelCommand               = new RelayCommand(_ => CancelOperation(),               _ => IsBusy);
        QuarantineAllCommand        = new RelayCommand(async _ => await QuarantineAllAsync(), _ => CanQuarantine);
        QuarantineSingleCommand     = new RelayCommand(async p => await QuarantineSingleAsync(p as ThreatInfo), _ => !IsQuarantining && !IsBusy);
        DismissThreatCommand        = new RelayCommand(p => DismissThreat(p as ThreatInfo));
        EmptyQuarantineCommand      = new RelayCommand(async _ => await EmptyQuarantineAsync(), _ => QuarantineEntries.Count > 0 && !IsQuarantining);
        RestoreAllQuarantineCommand = new RelayCommand(async _ => await RestoreAllQuarantineAsync(), _ => QuarantineEntries.Count > 0 && !IsQuarantining);
        ExportLogCommand            = new RelayCommand(_ => ExportLog());
        ClearLogCommand             = new RelayCommand(_ => ClearLog());
        RestoreCommand              = new RelayCommand(async p => await RestoreAsync(p as QuarantineEntry));
        DeleteCommand               = new RelayCommand(async p => await DeleteAsync(p as QuarantineEntry));
        RefreshQuarantineCommand    = new RelayCommand(async _ => await LoadQuarantineAsync());

        // Wire engine events to UI collections
        _clam.OutputReceived += (_, line) => Log(line);
        _clam.ThreatDetected += (_, threat) =>
        {
            RunOnUI(() =>
            {
                DetectedThreats.Add(threat);
                ThreatsFoundCount = DetectedThreats.Count;
            });
        };
        _clam.FileScanned += (_, e) =>
        {
            RunOnUI(() =>
            {
                FilesScannedCount = e.ScannedCount;
                string fn = Path.GetFileName(e.CurrentFile);
                CurrentScanningFile = string.IsNullOrEmpty(fn) ? e.CurrentFile : fn;
            });
        };
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Initialization  (called once from MainWindow.Loaded)
    // ═════════════════════════════════════════════════════════════════════════

    public async Task InitializeAsync()
    {
        LoadState();

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
            await RunUpdateAsync(force: true);
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
            await RunUpdateAsync(force: true);
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
            await RunUpdateAsync(force: true);
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

    public static string GetTargetDisplayName(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "Item";
        string trimmed = path.Trim('\"', ' ').TrimEnd('\\', '/');
        string name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? path.Trim('\"', ' ') : name;
    }

    public async Task RunFolderScanAsync()
    {
        if (IsBusy) return;

        if (ClamAVService.GetDatabaseDate() is null)
        {
            Log("Virus definitions are not installed yet. Starting initial update...");
            await RunUpdateAsync(force: true);
            if (ClamAVService.GetDatabaseDate() is null)
            {
                Log("Cannot scan: virus definitions could not be downloaded.");
                return;
            }
        }

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Folder to Scan — LibreScan Security",
            Multiselect = false,
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            string folder = dialog.FolderName;
            if (!Directory.Exists(folder))
            {
                Log($"Folder not found: {folder}");
                return;
            }

            string name = GetTargetDisplayName(folder);
            await RunScanCoreAsync([folder], $"Folder Scan ({name})");
        }
    }

    public async Task RunFileScanAsync()
    {
        if (IsBusy) return;

        if (ClamAVService.GetDatabaseDate() is null)
        {
            Log("Virus definitions are not installed yet. Starting initial update...");
            await RunUpdateAsync(force: true);
            if (ClamAVService.GetDatabaseDate() is null)
            {
                Log("Cannot scan: virus definitions could not be downloaded.");
                return;
            }
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select File(s) to Scan — LibreScan Security",
            Filter = "All Files (*.*)|*.*|Executables (*.exe;*.dll;*.scr;*.com;*.bat;*.cmd;*.ps1)|*.exe;*.dll;*.scr;*.com;*.bat;*.cmd;*.ps1|Archives (*.zip;*.rar;*.7z;*.tar;*.gz)|*.zip;*.rar;*.7z;*.tar;*.gz|Documents (*.doc;*.docx;*.docm;*.xls;*.xlsx;*.xlsm;*.pdf)|*.doc;*.docx;*.docm;*.xls;*.xlsx;*.xlsm;*.pdf",
            Multiselect = true,
        };

        if (dialog.ShowDialog() == true && dialog.FileNames.Length > 0)
        {
            var files = dialog.FileNames.Where(File.Exists).ToList();
            if (files.Count == 0) return;
            string label = files.Count == 1 ? $"File Scan ({GetTargetDisplayName(files[0])})" : $"File Scan ({files.Count} files)";
            await RunScanCoreAsync(files, label);
        }
    }

    public async Task HandleDroppedPathsAsync(string[] paths)
    {
        if (IsBusy || paths.Length == 0) return;

        if (ClamAVService.GetDatabaseDate() is null)
        {
            Log("Virus definitions are not installed yet. Starting initial update...");
            await RunUpdateAsync(force: true);
            if (ClamAVService.GetDatabaseDate() is null)
            {
                Log("Cannot scan: virus definitions could not be downloaded.");
                return;
            }
        }

        var validTargets = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (validTargets.Count == 0) return;

        string label = validTargets.Count == 1
            ? $"Drop Scan ({GetTargetDisplayName(validTargets[0])})"
            : $"Drop Scan ({validTargets.Count} items)";

        await RunScanCoreAsync(validTargets, label);
    }

    public async Task ScanPathFromExternalAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        string clean = path.Trim('\"', ' ');
        if (!File.Exists(clean) && !Directory.Exists(clean))
        {
            Log($"External target not found: {clean}");
            return;
        }

        if (IsBusy)
        {
            Log($"Cannot scan {clean}: another operation is in progress.");
            return;
        }

        if (ClamAVService.GetDatabaseDate() is null)
        {
            Log("Virus definitions are not installed yet. Starting initial update...");
            await RunUpdateAsync(force: true);
            if (ClamAVService.GetDatabaseDate() is null)
            {
                Log("Cannot scan: virus definitions could not be downloaded.");
                return;
            }
        }

        string label = File.Exists(clean)
            ? $"File Scan ({GetTargetDisplayName(clean)})"
            : $"Folder Scan ({GetTargetDisplayName(clean)})";

        await RunScanCoreAsync([clean], label);
    }

    public void OpenDriveSelector()
    {
        if (IsBusy) return;
        RefreshDrives();
        IsDriveSelectorOpen = true;
    }

    public void RefreshDrives()
    {
        RunOnUI(() =>
        {
            AvailableDrives.Clear();
            foreach (var d in ClamAVService.GetAvailableDrives())
                AvailableDrives.Add(d);
        });
    }

    public async Task RunSpecificDriveScanAsync(DriveItem? drive)
    {
        if (drive is null || IsBusy) return;
        IsDriveSelectorOpen = false;

        if (ClamAVService.GetDatabaseDate() is null)
        {
            Log("Virus definitions are not installed yet. Starting initial update...");
            await RunUpdateAsync(force: true);
            if (ClamAVService.GetDatabaseDate() is null)
            {
                Log("Cannot scan: virus definitions could not be downloaded.");
                return;
            }
        }

        if (!Directory.Exists(drive.Name))
        {
            Log($"Drive not accessible: {drive.Name}");
            return;
        }

        await RunScanCoreAsync([drive.Name], $"Drive Scan ({drive.DisplayName})");
    }

    private async Task RunScanCoreAsync(IReadOnlyList<string> targets, string label)
    {
        IsScanning = true;
        RunOnUI(() =>
        {
            DetectedThreats.Clear();
            ThreatsFoundCount   = 0;
            FilesScannedCount   = 0;
            ElapsedTime         = "00:00";
            CurrentScanningFile = "Initializing scan engine...";
        });
        _cts = new CancellationTokenSource();

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
            RunOnUI(() => CurrentScanningFile = string.Empty);

            if (result.Cancelled)
            {
                StatusText    = "Scan Cancelled";
                StatusSubtext = "Stopped by user";
                CurrentStatus = StatusLevel.Warning;
                Log("Scan cancelled.");
            }
            else if (result.MalwareDetected || result.ThreatsFound > 0)
            {
                var s = result.ThreatsFound != 1 ? "s" : "";
                StatusText    = $"{result.ThreatsFound} Threat{s} Found";
                StatusSubtext = result.Success ? "Review detected threats and take action" : "Review threats (some locked files skipped)";
                CurrentStatus = StatusLevel.Danger;
                Log($"{label} complete — {result.ThreatsFound} threat{s} in {result.FilesScanned} files.");
                SaveState(result.FilesScanned, result.ThreatsFound, label);
                ScanCompleted?.Invoke(result, label);
            }
            else if (result.ExitCode == 0 || (result.ExitCode == 2 && result.FilesScanned > 0) || result.Success)
            {
                string note = result.ExitCode == 2 ? " (some locked system files were skipped)" : "";
                StatusText    = "System Clean";
                StatusSubtext = $"No threats found · {result.FilesScanned} files scanned{note}";
                CurrentStatus = StatusLevel.Protected;
                Log($"{label} complete — no threats in {result.FilesScanned} files{note}.");
                SaveState(result.FilesScanned, result.ThreatsFound, label);
                ScanCompleted?.Invoke(result, label);
            }
            else
            {
                StatusText    = "Scan Error";
                StatusSubtext = $"ClamAV exit code {result.ExitCode}";
                CurrentStatus = StatusLevel.Error;
                Log($"Scan error (exit code {result.ExitCode}).");
                ScanCompleted?.Invoke(result, label);
            }
        }
        catch (Exception ex)
        {
            RunOnUI(() => CurrentScanningFile = string.Empty);
            StatusText    = "Scan Error";
            StatusSubtext = ex.Message;
            CurrentStatus = StatusLevel.Error;
            Log($"Scan error: {ex.Message}");
        }
        finally
        {
            try { _cts?.Cancel(); } catch { }
            _cts?.Dispose();
            _cts       = null;
            IsScanning = false;
            RunOnUI(() => CurrentScanningFile = string.Empty);
        }
    }

    private async Task TickElapsedAsync(Stopwatch sw, CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(ct))
            {
                RunOnUI(() => ElapsedTime = sw.Elapsed.ToString(@"mm\:ss"));
            }
        }
        catch (OperationCanceledException) { /* expected */ }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Update Definitions
    // ═════════════════════════════════════════════════════════════════════════

    public async Task RunUpdateAsync(bool force = false)
    {
        // Rate-limit definition updates to prevent Cisco CDN IP bans
        if (!force && ClamAVService.GetDatabaseDate() is not null)
        {
            var elapsed = DateTime.UtcNow - _lastUpdateAttemptUtc;
            if (elapsed < MinUpdateInterval)
            {
                var waitMins = Math.Max(1, (int)Math.Ceiling((MinUpdateInterval - elapsed).TotalMinutes));
                Log($"Definitions checked recently ({elapsed.TotalMinutes:F0}m ago). Waiting {waitMins}m to prevent IP ban.");
                StatusText    = "System Protected";
                StatusSubtext = $"Definitions are current · Last updated {DatabaseDate}";
                CurrentStatus = StatusLevel.Protected;
                return;
            }
        }

        _lastUpdateAttemptUtc = DateTime.UtcNow;
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
                UpdateCompleted?.Invoke(true);
            }
            else
            {
                StatusText    = "Update Failed";
                StatusSubtext = "Could not download virus definitions";
                CurrentStatus = StatusLevel.Warning;
                Log("Virus definition update failed.");
                UpdateCompleted?.Invoke(false);
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
            try { _cts?.Cancel(); } catch { }
            _cts?.Dispose();
            _cts       = null;
            IsUpdating = false;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Cancel & Process Shutdown
    // ═════════════════════════════════════════════════════════════════════════

    public void CancelOperation()
    {
        try { _cts?.Cancel(); } catch { }
        _clam.KillCurrentProcess();
        Log("Operation cancelled by user.");
    }

    public void Shutdown()
    {
        try { _cts?.Cancel(); } catch { }
        _clam.KillCurrentProcess();
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Quarantine
    // ═════════════════════════════════════════════════════════════════════════

    private async Task QuarantineAllAsync()
    {
        if (IsQuarantining || DetectedThreats.Count == 0) return;
        IsQuarantining = true;

        try
        {
            var threatsToProcess = DetectedThreats.ToList();
            int quarantinedCount = await _clam.QuarantineFilesAsync(threatsToProcess, (t, ok) =>
            {
                if (ok)
                {
                    Log($"Quarantined: {t.FilePath}");
                    RunOnUI(() => DetectedThreats.Remove(t));
                }
                else
                {
                    Log($"FAILED to quarantine: {t.FilePath} (file in use or requires elevation)");
                }
            });

            RunOnUI(() => ThreatsFoundCount = DetectedThreats.Count);
            await LoadQuarantineAsync();

            if (DetectedThreats.Count > 0)
            {
                StatusText    = $"{DetectedThreats.Count} Threat(s) Remain";
                StatusSubtext = "Some threats could not be quarantined (files locked or access denied)";
                CurrentStatus = StatusLevel.Danger;
                Log($"WARNING: {quarantinedCount} quarantined, {DetectedThreats.Count} threat(s) could not be quarantined.");
            }
            else
            {
                StatusText    = "Threats Quarantined";
                StatusSubtext = $"{quarantinedCount} file(s) isolated in secure quarantine";
                CurrentStatus = StatusLevel.Protected;
                Log($"{quarantinedCount} file(s) moved to quarantine.");
            }
        }
        finally
        {
            IsQuarantining = false;
        }
    }

    private async Task LoadQuarantineAsync()
    {
        var entries = await _clam.GetQuarantineEntriesAsync();
        RunOnUI(() =>
        {
            QuarantineEntries.Clear();
            foreach (var e in entries) QuarantineEntries.Add(e);
        });
    }

    private async Task RestoreAsync(QuarantineEntry? entry)
    {
        if (entry is null) return;
        lock (_activeQuarantineOperations)
        {
            if (!_activeQuarantineOperations.Add(entry.Id)) return;
        }

        try
        {
            if (await _clam.RestoreFromQuarantineAsync(entry))
            {
                Log($"Restored: {entry.OriginalPath}");
            }
            else
            {
                Log($"Failed to restore {entry.OriginalFileName}. File may be missing or target path requires elevated permissions.");
            }
            await LoadQuarantineAsync();
        }
        finally
        {
            lock (_activeQuarantineOperations)
            {
                _activeQuarantineOperations.Remove(entry.Id);
            }
        }
    }

    private async Task DeleteAsync(QuarantineEntry? entry)
    {
        if (entry is null) return;
        lock (_activeQuarantineOperations)
        {
            if (!_activeQuarantineOperations.Add(entry.Id)) return;
        }

        try
        {
            if (await _clam.DeleteFromQuarantineAsync(entry))
            {
                Log($"Permanently deleted: {entry.OriginalFileName}");
            }
            else
            {
                Log($"Failed to delete {entry.OriginalFileName} from quarantine.");
            }
            await LoadQuarantineAsync();
        }
        finally
        {
            lock (_activeQuarantineOperations)
            {
                _activeQuarantineOperations.Remove(entry.Id);
            }
        }
    }

    public async Task QuarantineSingleAsync(ThreatInfo? threat)
    {
        if (threat is null || IsQuarantining) return;
        IsQuarantining = true;

        try
        {
            bool ok = await _clam.QuarantineFileAsync(threat);
            if (ok)
            {
                Log($"Quarantined: {threat.FilePath}");
                RunOnUI(() =>
                {
                    DetectedThreats.Remove(threat);
                    ThreatsFoundCount = DetectedThreats.Count;
                });
                await LoadQuarantineAsync();

                if (DetectedThreats.Count == 0)
                {
                    StatusText    = "Threat Quarantined";
                    StatusSubtext = "File isolated in secure quarantine";
                    CurrentStatus = StatusLevel.Protected;
                }
            }
            else
            {
                Log($"FAILED to quarantine: {threat.FilePath} (file in use or requires elevation)");
            }
        }
        finally
        {
            IsQuarantining = false;
        }
    }

    public void DismissThreat(ThreatInfo? threat)
    {
        if (threat is null) return;
        RunOnUI(() =>
        {
            DetectedThreats.Remove(threat);
            ThreatsFoundCount = DetectedThreats.Count;
        });
        Log($"Dismissed detection: {threat.FilePath}");

        if (DetectedThreats.Count == 0 && CurrentStatus == StatusLevel.Danger)
        {
            StatusText    = "Detections Handled";
            StatusSubtext = "No remaining active threats in current scan session";
            CurrentStatus = StatusLevel.Protected;
        }
    }

    public async Task EmptyQuarantineAsync(bool skipConfirmation = false)
    {
        if (IsQuarantining || QuarantineEntries.Count == 0) return;

        if (!skipConfirmation)
        {
            var confirm = MessageBox.Show(
                "Are you sure you want to permanently delete all items in quarantine? This cannot be undone.",
                "Empty Quarantine — LibreScan",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;
        }

        IsQuarantining = true;
        try
        {
            int count = await _clam.DeleteAllFromQuarantineAsync();
            await LoadQuarantineAsync();
            Log($"Quarantine emptied ({count} files permanently removed).");
        }
        finally
        {
            IsQuarantining = false;
        }
    }

    public async Task RestoreAllQuarantineAsync(bool skipConfirmation = false)
    {
        if (IsQuarantining || QuarantineEntries.Count == 0) return;

        if (!skipConfirmation)
        {
            var confirm = MessageBox.Show(
                "Are you sure you want to restore all quarantined files to their original directories?",
                "Restore All — LibreScan",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;
        }

        IsQuarantining = true;
        try
        {
            var (restored, failed) = await _clam.RestoreAllFromQuarantineAsync();
            await LoadQuarantineAsync();
            Log($"Batch restore completed: {restored} restored, {failed} failed.");
        }
        finally
        {
            IsQuarantining = false;
        }
    }

    public void ExportLog()
    {
        if (LogEntries.Count == 0)
        {
            MessageBox.Show("Activity log is currently empty.", "Export Log", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Activity Log — LibreScan Security",
            Filter = "Text Files (*.txt)|*.txt|Log Files (*.log)|*.log|All Files (*.*)|*.*",
            FileName = $"LibreScan_Log_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var lines = LogEntries.ToList();
                File.WriteAllLines(dialog.FileName, lines);
                Log($"Log exported to: {dialog.FileName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to export log: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public void ClearLog()
    {
        RunOnUI(() => LogEntries.Clear());
    }

    // ── State Persistence (Last Scan Summary) ────────────────────────────────

    private static string GetStateFilePath()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LibreScan");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "state.json");
    }

    private void LoadState()
    {
        try
        {
            string path = GetStateFilePath();
            if (!File.Exists(path)) return;
            string json = File.ReadAllText(path);
            var state = System.Text.Json.JsonSerializer.Deserialize<AppState>(json);
            if (state?.LastScanDate is not null)
            {
                string timeStr = state.LastScanDate.Value.ToLocalTime().ToString("MMM d, HH:mm");
                LastScanSummary = $"{timeStr} ({state.LastScanFiles:N0} files, {state.LastScanThreats} threats)";
            }
        }
        catch { }
    }

    private void SaveState(int files, int threats, string scanType)
    {
        try
        {
            var state = new AppState
            {
                LastScanDate = DateTime.UtcNow,
                LastScanFiles = files,
                LastScanThreats = threats,
                LastScanType = scanType
            };
            string json = System.Text.Json.JsonSerializer.Serialize(state, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(GetStateFilePath(), json);
            string timeStr = state.LastScanDate.Value.ToLocalTime().ToString("MMM d, HH:mm");
            RunOnUI(() => LastScanSummary = $"{timeStr} ({files:N0} files, {threats} threats)");
        }
        catch { }
    }

    private static void InvalidateCommands()
    {
        RunOnUI(CommandManager.InvalidateRequerySuggested);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════════════════

    private void RefreshDatabaseDate()
    {
        var d = ClamAVService.GetDatabaseDate();
        DatabaseDate = d?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "Not downloaded";
    }

    private const int MaxLogEntries = 2000;
    private const int LogBatchTrimThreshold = 2200;

    private static void RunOnUI(Action action)
    {
        try
        {
            if (Application.Current?.Dispatcher is { } d && !d.CheckAccess())
                d.InvokeAsync(action);
            else
                action();
        }
        catch { }
    }

    private void Log(string message)
    {
        void Append()
        {
            var ts = DateTime.Now.ToString("HH:mm:ss");
            LogEntries.Add($"[{ts}] {message}");

            // Batch trim instead of O(N) array shift on every single line
            if (LogEntries.Count > LogBatchTrimThreshold)
            {
                int itemsToRemove = LogEntries.Count - MaxLogEntries;
                for (int i = 0; i < itemsToRemove; i++)
                    LogEntries.RemoveAt(0);
            }
        }

        RunOnUI(Append);
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
