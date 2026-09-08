using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using LibreScan.Models;

namespace LibreScan.Services;

/// <summary>
/// Wraps ClamAV command-line tools (clamscan.exe, freshclam.exe) with proper
/// async threading, UI-throttled output, zombie-process protection, and
/// quarantine management.
/// </summary>
public sealed class ClamAVService
{
    // ── Path Resolution (AppContext.BaseDirectory for single-file deploy) ────
    private static readonly string BaseDir       = AppContext.BaseDirectory;
    private static readonly string ClamScanPath  = Path.Combine(BaseDir, "clamav_bin", "clamscan.exe");
    private static readonly string FreshClamPath = Path.Combine(BaseDir, "clamav_bin", "freshclam.exe");
    private static readonly string FreshClamConf = Path.Combine(BaseDir, "clamav_bin", "freshclam.conf");
    private static readonly string DatabaseDir   = Path.Combine(BaseDir, "clamav_bin", "database");
    private static readonly string QuarantineDir = Path.Combine(BaseDir, "Quarantine");
    private static readonly string ManifestPath  = Path.Combine(BaseDir, "Quarantine", "manifest.json");

    // ── ClamAV output parsing ────────────────────────────────────────────────
    private static readonly Regex ThreatRegex   = new(@"^(?<path>.+?):\s+(?<name>.+?)\s+FOUND\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ScannedRegex  = new(@"Scanned files:\s+(\d+)",       RegexOptions.Compiled);

    // ── UI Throttle (CRITICAL — prevents Dispatcher flooding) ────────────────
    private readonly Stopwatch _throttle = new();
    private Process? _currentProcess;

    // ── Events ───────────────────────────────────────────────────────────────
    public event EventHandler<string>?     OutputReceived;
    public event EventHandler<ThreatInfo>? ThreatDetected;

    // ─────────────────────────────────────────────────────────────────────────
    // Engine & Database Status
    // ─────────────────────────────────────────────────────────────────────────

    public static bool IsEngineAvailable() => File.Exists(ClamScanPath);

    /// <summary>
    /// IP-Ban Prevention: only returns true when the database folder is missing
    /// or every .cvd/.cld file is older than 24 hours.
    /// </summary>
    public static bool IsUpdateNeeded()
    {
        if (!Directory.Exists(DatabaseDir)) return true;

        var cvdFiles = Directory.GetFiles(DatabaseDir, "*.cvd")
            .Concat(Directory.GetFiles(DatabaseDir, "*.cld"))
            .ToArray();

        // If no virus signature databases exist at all, update is definitely required
        if (cvdFiles.Length == 0) return true;

        // Check the newest database or freshclam record
        var allRecords = cvdFiles
            .Concat(Directory.GetFiles(DatabaseDir, "freshclam.dat"))
            .ToArray();

        var newest = allRecords.Max(f => File.GetLastWriteTimeUtc(f));
        return (DateTime.UtcNow - newest).TotalHours > 24;
    }

    public static DateTime? GetDatabaseDate()
    {
        if (!Directory.Exists(DatabaseDir)) return null;

        var dbFiles = Directory.GetFiles(DatabaseDir, "*.cvd")
            .Concat(Directory.GetFiles(DatabaseDir, "*.cld"))
            .ToArray();

        return dbFiles.Length == 0
            ? null
            : dbFiles.Max(f => File.GetLastWriteTimeUtc(f));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Quick / Full Scan Target Resolution
    // ─────────────────────────────────────────────────────────────────────────

    public static IReadOnlyList<string> GetQuickScanTargets()
    {
        var targets = new List<string>();
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        foreach (var sub in new[] { "Downloads", "Desktop", "Documents" })
        {
            var path = Path.Combine(profile, sub);
            if (Directory.Exists(path)) targets.Add(path);
        }

        var temp = Path.GetTempPath();
        if (Directory.Exists(temp)) targets.Add(temp);

        return targets;
    }

    public static IReadOnlyList<string> GetFullScanTargets()
    {
        return DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => d.RootDirectory.FullName)
            .ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Scan
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<ScanResult> RunScanAsync(
        IReadOnlyList<string> targetPaths,
        CancellationToken ct = default)
    {
        if (!File.Exists(ClamScanPath))
            throw new FileNotFoundException("ClamAV clamscan.exe not found.", ClamScanPath);

        var threats = new List<ThreatInfo>();
        var log     = new StringBuilder();
        _throttle.Reset();

        var psi = new ProcessStartInfo
        {
            FileName               = ClamScanPath,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            WorkingDirectory       = BaseDir,
            StandardOutputEncoding = Encoding.UTF8,      // Unicode fix
            StandardErrorEncoding  = Encoding.UTF8,
        };

        psi.ArgumentList.Add($"--database={DatabaseDir}");
        psi.ArgumentList.Add("--recursive");
        psi.ArgumentList.Add("--infected");
        psi.ArgumentList.Add("--stdout");
        foreach (var t in targetPaths)
            psi.ArgumentList.Add(t);

        var timer = Stopwatch.StartNew();

        using var process = new Process { StartInfo = psi };
        _currentProcess = process;

        // ── OutputDataReceived with Stopwatch throttle (100 ms) ──────────
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            string line = e.Data;
            log.AppendLine(line);

            // ALWAYS dispatch threat lines immediately (no throttle)
            var match = ThreatRegex.Match(line);
            if (match.Success)
            {
                var threat = new ThreatInfo
                {
                    FilePath   = (match.Groups["path"].Success ? match.Groups["path"].Value : match.Groups[1].Value).Trim(),
                    ThreatName = (match.Groups["name"].Success ? match.Groups["name"].Value : match.Groups[2].Value).Trim(),
                    DetectedAt = DateTime.UtcNow,
                };
                lock (threats) { threats.Add(threat); }

                Application.Current?.Dispatcher.InvokeAsync(() =>
                    ThreatDetected?.Invoke(this, threat));
                return;
            }

            // Throttle general output to ≤ 10 updates/sec
            if (!_throttle.IsRunning || _throttle.ElapsedMilliseconds >= 100)
            {
                _throttle.Restart();
                Application.Current?.Dispatcher.InvokeAsync(() =>
                    OutputReceived?.Invoke(this, line));
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            string line = e.Data.Trim();
            log.AppendLine($"[stderr] {line}");
            Application.Current?.Dispatcher.InvokeAsync(() =>
                OutputReceived?.Invoke(this, line));
        };

        // ── Zombie Fix: kill entire process tree on cancellation ─────────
        using var killReg = ct.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { /* already exited */ }
        });

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
            process.WaitForExit(); // Flush remaining redirected async output buffers
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { /* already exited */ }
            timer.Stop();
            _currentProcess = null;
            return new ScanResult
            {
                Cancelled = true,
                Duration  = timer.Elapsed,
                FullLog   = log.ToString(),
            };
        }

        timer.Stop();
        _currentProcess = null;

        int exitCode = process.ExitCode;

        // Parse "Scanned files: N" from summary
        int filesScanned = 0;
        var scannedMatch = ScannedRegex.Match(log.ToString());
        if (scannedMatch.Success)
            filesScanned = int.Parse(scannedMatch.Groups[1].Value);

        // Flush a final status line to the UI
        Application.Current?.Dispatcher.InvokeAsync(() =>
            OutputReceived?.Invoke(this, $"── Scan finished (exit {exitCode}) ──"));

        // Exit Code 1 Trap: ExitCode == 1 means malware found, NOT a failure
        return new ScanResult
        {
            Success         = exitCode is 0 or 1,
            MalwareDetected = exitCode == 1,
            ExitCode        = exitCode,
            ThreatsFound    = threats.Count,
            FilesScanned    = filesScanned,
            Threats         = [.. threats],
            Duration        = timer.Elapsed,
            FullLog         = log.ToString(),
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Update Definitions (freshclam)
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<bool> UpdateDefinitionsAsync(CancellationToken ct = default)
    {
        if (!File.Exists(FreshClamPath))
            throw new FileNotFoundException("freshclam.exe not found.", FreshClamPath);

        Directory.CreateDirectory(DatabaseDir);

        var psi = new ProcessStartInfo
        {
            FileName               = FreshClamPath,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            WorkingDirectory       = Path.Combine(BaseDir, "clamav_bin"),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8,
        };

        if (File.Exists(FreshClamConf))
            psi.ArgumentList.Add($"--config-file={FreshClamConf}");
        psi.ArgumentList.Add($"--datadir={DatabaseDir}");
        psi.ArgumentList.Add("--show-progress=no");

        using var process = new Process { StartInfo = psi };
        _currentProcess = process;

        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            string line = e.Data.Trim();
            Application.Current?.Dispatcher.InvokeAsync(() =>
                OutputReceived?.Invoke(this, line));
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            string line = e.Data.Trim();
            Application.Current?.Dispatcher.InvokeAsync(() =>
                OutputReceived?.Invoke(this, line));
        };

        using var killReg = ct.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { }
        });

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
            process.WaitForExit();
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { }
            _currentProcess = null;
            return false;
        }

        _currentProcess = null;
        return process.ExitCode == 0;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Quarantine Management
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<bool> QuarantineFileAsync(ThreatInfo threat)
    {
        if (!File.Exists(threat.FilePath)) return false;
        Directory.CreateDirectory(QuarantineDir);

        var entry = new QuarantineEntry
        {
            Id                = Guid.NewGuid().ToString("N"),
            OriginalPath      = threat.FilePath,
            OriginalFileName  = Path.GetFileName(threat.FilePath),
            ThreatName        = threat.ThreatName,
            QuarantinedAt     = DateTime.UtcNow,
        };
        entry.QuarantinedFileName = $"{entry.Id}.quarantine";

        try
        {
            File.Move(threat.FilePath, Path.Combine(QuarantineDir, entry.QuarantinedFileName));
        }
        catch { return false; }

        var manifest = await LoadManifestAsync();
        manifest.Add(entry);
        await SaveManifestAsync(manifest);
        return true;
    }

    public async Task<List<QuarantineEntry>> GetQuarantineEntriesAsync()
        => await LoadManifestAsync();

    public async Task<bool> RestoreFromQuarantineAsync(QuarantineEntry entry)
    {
        var src = Path.Combine(QuarantineDir, entry.QuarantinedFileName);
        if (!File.Exists(src)) return false;

        try
        {
            var dir = Path.GetDirectoryName(entry.OriginalPath);
            if (dir is not null) Directory.CreateDirectory(dir);
            File.Move(src, entry.OriginalPath, overwrite: true);
        }
        catch { return false; }

        var manifest = await LoadManifestAsync();
        manifest.RemoveAll(e => e.Id == entry.Id);
        await SaveManifestAsync(manifest);
        return true;
    }

    public async Task<bool> DeleteFromQuarantineAsync(QuarantineEntry entry)
    {
        try
        {
            var path = Path.Combine(QuarantineDir, entry.QuarantinedFileName);
            if (File.Exists(path)) File.Delete(path);
        }
        catch { return false; }

        var manifest = await LoadManifestAsync();
        manifest.RemoveAll(e => e.Id == entry.Id);
        await SaveManifestAsync(manifest);
        return true;
    }

    // ── Manifest I/O ─────────────────────────────────────────────────────────

    private static async Task<List<QuarantineEntry>> LoadManifestAsync()
    {
        if (!File.Exists(ManifestPath))
            return [];

        var json = await File.ReadAllTextAsync(ManifestPath);
        return JsonSerializer.Deserialize<List<QuarantineEntry>>(json) ?? [];
    }

    private static async Task SaveManifestAsync(List<QuarantineEntry> entries)
    {
        Directory.CreateDirectory(QuarantineDir);
        var json = JsonSerializer.Serialize(entries,
            new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(ManifestPath, json);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Emergency Kill
    // ─────────────────────────────────────────────────────────────────────────

    public void KillCurrentProcess()
    {
        try
        {
            if (_currentProcess is { HasExited: false })
                _currentProcess.Kill(entireProcessTree: true);
        }
        catch { /* already exited */ }
    }
}
