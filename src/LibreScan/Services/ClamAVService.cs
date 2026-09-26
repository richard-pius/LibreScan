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
public sealed partial class ClamAVService
{
    // ── Path Resolution (supports single-file publish and dev/test environments) ────
    public static string BaseDir       { get; } = ResolveBaseDirectory();
    public static string ClamScanPath  { get; } = Path.Combine(BaseDir, "clamav_bin", "clamscan.exe");
    public static string FreshClamPath { get; } = Path.Combine(BaseDir, "clamav_bin", "freshclam.exe");
    public static string FreshClamConf { get; } = Path.Combine(BaseDir, "clamav_bin", "freshclam.conf");
    public static string DatabaseDir   { get; } = Path.Combine(BaseDir, "clamav_bin", "database");
    public static string QuarantineDir { get; } = Path.Combine(BaseDir, "Quarantine");
    public static string ManifestPath  { get; } = Path.Combine(BaseDir, "Quarantine", "manifest.json");

    private static string ResolveBaseDirectory()
    {
        var baseDir = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(baseDir, "clamav_bin", "clamscan.exe")))
            return baseDir;

        // Search parent directories up to 5 levels for development / testing environments
        var dir = new DirectoryInfo(baseDir);
        for (int i = 0; i < 5 && dir.Parent != null; i++)
        {
            dir = dir.Parent;
            if (File.Exists(Path.Combine(dir.FullName, "clamav_bin", "clamscan.exe")))
                return dir.FullName;
        }

        return baseDir;
    }

    // ── High-Performance Source-Generated Regexes ─────────────────────────────
    [GeneratedRegex(@"^(?<path>.+?):\s+(?<name>.+?)\s+FOUND\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex GeneratedThreatRegex();

    [GeneratedRegex(@"Scanned files:\s+(\d+)")]
    private static partial Regex GeneratedScannedRegex();

    [GeneratedRegex(@"Infected files:\s+(\d+)")]
    private static partial Regex GeneratedInfectedRegex();

    public static Regex ThreatRegex => GeneratedThreatRegex();
    public static Regex ScannedRegex => GeneratedScannedRegex();
    public static Regex InfectedRegex => GeneratedInfectedRegex();

    private static readonly HashSet<string> ProtectedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "svchost", "lsass", "csrss", "smss", "services", "wininit",
        "winlogon", "dwm", "explorer", "System", "ntoskrnl",
        "conhost", "RuntimeBroker", "SearchHost", "StartMenuExperienceHost",
        "ShellExperienceHost", "sihost", "fontdrvhost", "WmiPrvSE"
    };

    private static readonly string[] ProtectedPaths = 
    {
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64"),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
    };

    // ── Concurrency, Caches & Process State ──────────────────────────────────
    private long _lastOutputTick;
    private readonly object _processLock = new();
    private Process? _currentProcess;
    private static readonly SemaphoreSlim ManifestLock = new(1, 1);
    private static readonly JsonSerializerOptions JsonIndentedOptions = new() { WriteIndented = true };

    // ── Events ───────────────────────────────────────────────────────────────
    public event EventHandler<string>?     OutputReceived;
    public event EventHandler<ThreatInfo>? ThreatDetected;
    public event EventHandler<(int ScannedCount, string CurrentFile)>? FileScanned;

    private void DispatchEvent(Action action)
    {
        try
        {
            if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
            {
                dispatcher.InvokeAsync(action);
            }
            else
            {
                action();
            }
        }
        catch
        {
            // Defensive against dispatch errors during shutdown
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Parsing Helpers (Zero-Allocation Optimized)
    // ─────────────────────────────────────────────────────────────────────────

    public static ThreatInfo? ParseThreatLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var match = GeneratedThreatRegex().Match(line);
        if (!match.Success) return null;

        string rawPath = (match.Groups["path"].Success ? match.Groups["path"].Value : match.Groups[1].Value).Trim();
        string threatName = (match.Groups["name"].Success ? match.Groups["name"].Value : match.Groups[2].Value).Trim();

        // Non-allocating threat name cleanup if archive separator present
        int lastColon = threatName.LastIndexOf(": ", StringComparison.Ordinal);
        if (lastColon >= 0)
        {
            threatName = threatName[(lastColon + 2)..].Trim();
        }

        return new ThreatInfo
        {
            FilePath   = rawPath,
            ThreatName = threatName,
            DetectedAt = DateTime.UtcNow,
        };
    }

    public static int ParseScannedFiles(string log)
    {
        var match = GeneratedScannedRegex().Match(log);
        return match.Success && int.TryParse(match.Groups[1].Value, out int count) ? count : 0;
    }

    public static int ParseInfectedFiles(string log)
    {
        var match = GeneratedInfectedRegex().Match(log);
        return match.Success && int.TryParse(match.Groups[1].Value, out int count) ? count : 0;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Engine & Database Status (Single-Pass Directory Enumeration)
    // ─────────────────────────────────────────────────────────────────────────

    public static bool IsEngineAvailable() => File.Exists(ClamScanPath);

    /// <summary>
    /// IP-Ban Prevention: returns true when the database folder is missing,
    /// definitions are missing, or every definition record is older than 24 hours.
    /// Uses zero-allocation cached file metadata.
    /// </summary>
    public static bool IsUpdateNeeded()
    {
        if (!Directory.Exists(DatabaseDir)) return true;

        var dirInfo = new DirectoryInfo(DatabaseDir);
        DateTime newestUtc = DateTime.MinValue;
        bool hasDefinitions = false;

        foreach (var file in dirInfo.EnumerateFiles())
        {
            string ext = file.Extension;
            if (ext.Equals(".cvd", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".cld", StringComparison.OrdinalIgnoreCase))
            {
                hasDefinitions = true;
                if (file.LastWriteTimeUtc > newestUtc)
                    newestUtc = file.LastWriteTimeUtc;
            }
            else if (file.Name.Equals("freshclam.dat", StringComparison.OrdinalIgnoreCase))
            {
                if (file.LastWriteTimeUtc > newestUtc)
                    newestUtc = file.LastWriteTimeUtc;
            }
        }

        if (!hasDefinitions || newestUtc == DateTime.MinValue)
            return true;

        return (DateTime.UtcNow - newestUtc).TotalHours > 24;
    }

    public static DateTime? GetDatabaseDate()
    {
        if (!Directory.Exists(DatabaseDir)) return null;

        var dirInfo = new DirectoryInfo(DatabaseDir);
        DateTime? newest = null;

        foreach (var file in dirInfo.EnumerateFiles())
        {
            string ext = file.Extension;
            if (ext.Equals(".cvd", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".cld", StringComparison.OrdinalIgnoreCase))
            {
                if (newest is null || file.LastWriteTimeUtc > newest.Value)
                    newest = file.LastWriteTimeUtc;
            }
        }

        return newest;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Quick / Full Scan Target Resolution
    // ─────────────────────────────────────────────────────────────────────────

    public static IReadOnlyList<string> GetQuickScanTargets()
    {
        var targets = new List<string>();
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (!string.IsNullOrWhiteSpace(profile))
        {
            foreach (var sub in new[] { "Downloads", "Desktop", "Documents" })
            {
                var path = Path.Combine(profile, sub);
                if (Directory.Exists(path)) targets.Add(path);
            }

            var startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (Directory.Exists(startup)) targets.Add(startup);
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

    /// <summary>
    /// Enumerates all ready system storage drives (fixed, removable, external, network)
    /// with capacity metadata for user selection.
    /// </summary>
    public static List<DriveItem> GetAvailableDrives()
    {
        var items = new List<DriveItem>();
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch
        {
            return items;
        }

        foreach (var drive in drives)
        {
            try
            {
                if (!drive.IsReady) continue;

                string volumeLabel = string.Empty;
                try { volumeLabel = drive.VolumeLabel; } catch { }
                if (string.IsNullOrWhiteSpace(volumeLabel))
                    volumeLabel = drive.DriveType == DriveType.Fixed ? "Local Disk" : "Drive";

                string typeDesc = drive.DriveType switch
                {
                    DriveType.Fixed     => "Fixed Drive (Internal)",
                    DriveType.Removable => "Removable USB / Flash Drive",
                    DriveType.Network   => "Network Drive",
                    DriveType.CDRom     => "CD / DVD-ROM",
                    _                   => "Storage Drive"
                };

                double totalBytes = 0;
                double freeBytes = 0;
                try
                {
                    totalBytes = drive.TotalSize;
                    freeBytes  = drive.AvailableFreeSpace;
                }
                catch { }

                double freePct = totalBytes > 0 ? (freeBytes / totalBytes) * 100.0 : 0.0;
                string capacityDesc = totalBytes > 0
                    ? $"{FormatBytes((long)freeBytes)} free of {FormatBytes((long)totalBytes)}"
                    : "Ready";

                string rootName = drive.RootDirectory.FullName;
                string driveLetter = rootName.TrimEnd('\\');

                items.Add(new DriveItem
                {
                    Name                 = rootName,
                    VolumeLabel          = volumeLabel,
                    DisplayName          = $"{driveLetter} [{volumeLabel}]",
                    DriveTypeDescription = typeDesc,
                    CapacityDescription  = capacityDesc,
                    FreePercentage       = freePct,
                    IsReady              = true,
                });
            }
            catch
            {
                // Isolate unreadable, locked, or optical drive errors from aborting enumeration
            }
        }

        return items;
    }

    /// <summary>
    /// Formats a byte count into a human-readable string (B, KB, MB, GB, TB).
    /// </summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        if (bytes < 1024L * 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0 * 1024.0):F1} TB";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Scan
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<ScanResult> RunScanAsync(
        IReadOnlyList<string> targetPaths,
        CancellationToken ct = default)
    {
        if (targetPaths is null || targetPaths.Count == 0)
        {
            return new ScanResult
            {
                Success = true,
                Duration = TimeSpan.Zero,
                FullLog = "No scan targets specified.",
            };
        }

        var validTargets = new List<string>();
        foreach (var t in targetPaths)
        {
            if (string.IsNullOrWhiteSpace(t)) continue;
            string clean = t.Trim('\"', ' ');
            if (!string.IsNullOrEmpty(clean))
                validTargets.Add(clean);
        }

        if (validTargets.Count == 0)
        {
            return new ScanResult
            {
                Success = true,
                Duration = TimeSpan.Zero,
                FullLog = "No valid scan targets specified.",
            };
        }

        if (!File.Exists(ClamScanPath))
            throw new FileNotFoundException("ClamAV clamscan.exe not found.", ClamScanPath);

        // Ensure database directory exists so clamscan does not fail with code 2 on fresh installs
        Directory.CreateDirectory(DatabaseDir);

        var threats = new List<ThreatInfo>();
        var log     = new StringBuilder();
        int? streamedFilesScanned = null;
        int? streamedInfectedFiles = null;
        int liveScannedFiles = 0;
        Interlocked.Exchange(ref _lastOutputTick, 0);
        long lastFileScannedTick = 0;

        var psi = new ProcessStartInfo
        {
            FileName               = ClamScanPath,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            WorkingDirectory       = BaseDir,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8,
        };

        psi.ArgumentList.Add($"--database={DatabaseDir}");
        psi.ArgumentList.Add("--recursive");
        psi.ArgumentList.Add("--stdout");

        // Exclude Quarantine directory to prevent false-positive detection loops
        psi.ArgumentList.Add("--exclude-dir=Quarantine");

        // Exclude Windows system volume and locked OS swap files
        psi.ArgumentList.Add("--exclude-dir=System Volume Information");
        psi.ArgumentList.Add("--exclude-dir=\\$Recycle\\.Bin");
        psi.ArgumentList.Add(@"--exclude=(pagefile|hiberfil|swapfile|dumpstack)\.sys");

        foreach (var target in validTargets)
        {
            psi.ArgumentList.Add(target);
        }

        var timer = Stopwatch.StartNew();

        using var process = new Process { StartInfo = psi };
        lock (_processLock)
        {
            _currentProcess = process;
        }

        // ── OutputDataReceived with live progress tracking & tick-throttled log ────
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            string line = e.Data;
            lock (log) { log.AppendLine(line); }

            // Check if threat detected
            var threat = ParseThreatLine(line);
            if (threat is not null)
            {
                lock (threats) { threats.Add(threat); }
                int currentCount = Interlocked.Increment(ref liveScannedFiles);
                DispatchEvent(() => ThreatDetected?.Invoke(this, threat));
                DispatchEvent(() => FileScanned?.Invoke(this, (currentCount, threat.FilePath)));
                DispatchEvent(() => OutputReceived?.Invoke(this, line));
                return;
            }

            // Stream-parse summary counts
            if (line.StartsWith("Scanned files:", StringComparison.OrdinalIgnoreCase))
            {
                var match = GeneratedScannedRegex().Match(line);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int count))
                    streamedFilesScanned = count;
                DispatchEvent(() => OutputReceived?.Invoke(this, line));
                return;
            }
            if (line.StartsWith("Infected files:", StringComparison.OrdinalIgnoreCase))
            {
                var match = GeneratedInfectedRegex().Match(line);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int count))
                    streamedInfectedFiles = count;
                DispatchEvent(() => OutputReceived?.Invoke(this, line));
                return;
            }

            // Detect file scan lines (e.g. "path: OK", "path: ... ERROR", etc.)
            if (line.EndsWith(": OK", StringComparison.OrdinalIgnoreCase) ||
                line.EndsWith(" ERROR", StringComparison.OrdinalIgnoreCase) ||
                line.EndsWith(" Empty file", StringComparison.OrdinalIgnoreCase) ||
                line.EndsWith(" Excluded", StringComparison.OrdinalIgnoreCase))
            {
                int currentCount = Interlocked.Increment(ref liveScannedFiles);
                int colonIdx = line.LastIndexOf(": ", StringComparison.Ordinal);
                string scannedFilePath = colonIdx > 0 ? line[..colonIdx].Trim() : line;

                long now = Environment.TickCount64;
                if (now - Interlocked.Read(ref lastFileScannedTick) >= 50)
                {
                    Interlocked.Exchange(ref lastFileScannedTick, now);
                    DispatchEvent(() => FileScanned?.Invoke(this, (currentCount, scannedFilePath)));
                }

                // Throttle regular OK / clean file lines in the activity log to ≤ 5 updates/sec
                if (now - Interlocked.Read(ref _lastOutputTick) >= 200)
                {
                    Interlocked.Exchange(ref _lastOutputTick, now);
                    DispatchEvent(() => OutputReceived?.Invoke(this, line));
                }
                return;
            }

            // For summary headers, errors, etc., log directly
            DispatchEvent(() => OutputReceived?.Invoke(this, line));
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            string line = e.Data.Trim();
            lock (log) { log.AppendLine($"[stderr] {line}"); }
            DispatchEvent(() => OutputReceived?.Invoke(this, line));
        };

        // ── Kill entire process tree on cancellation ─────────────────────
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
            lock (_processLock) { _currentProcess = null; }
            return new ScanResult
            {
                Cancelled = true,
                Duration  = timer.Elapsed,
                FullLog   = log.ToString(),
            };
        }
        finally
        {
            lock (_processLock) { _currentProcess = null; }
        }

        timer.Stop();

        int exitCode = process.ExitCode;
        string fullLog = log.ToString();
        int filesScanned = streamedFilesScanned ?? (liveScannedFiles > 0 ? liveScannedFiles : ParseScannedFiles(fullLog));
        int infectedFiles = streamedInfectedFiles ?? ParseInfectedFiles(fullLog);
        int threatsFound = Math.Max(threats.Count, infectedFiles);
        bool hasThreats = threatsFound > 0;
        bool isClean = exitCode == 0 || (exitCode == 2 && threatsFound == 0 && filesScanned > 0);

        // Flush final status and exact final file count
        DispatchEvent(() => FileScanned?.Invoke(this, (filesScanned, string.Empty)));
        DispatchEvent(() =>
            OutputReceived?.Invoke(this, $"── Scan finished (exit {exitCode}) ──"));

        // Exit Code 0 = clean, Exit Code 1 = malware found.
        // Exit Code 2 = error (e.g. locked system files), but threats may still have been detected!
        return new ScanResult
        {
            Success         = isClean || (exitCode == 1 && threatsFound > 0),
            MalwareDetected = hasThreats || exitCode == 1,
            ExitCode        = exitCode,
            ThreatsFound    = threatsFound,
            FilesScanned    = filesScanned,
            Threats         = [.. threats],
            Duration        = timer.Elapsed,
            FullLog         = fullLog,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Update Definitions (freshclam)
    // ─────────────────────────────────────────────────────────────────────────

    public static void EnsureFreshClamConfig()
    {
        try
        {
            var dir = Path.GetDirectoryName(FreshClamConf);
            if (dir is not null) Directory.CreateDirectory(dir);

            if (!File.Exists(FreshClamConf))
            {
                var minimalConf = "DatabaseMirror database.clamav.net\r\nDNSDatabaseInfo current.cvd.clamav.net\r\n";
                var utf8NoBom = new UTF8Encoding(false);
                File.WriteAllText(FreshClamConf, minimalConf, utf8NoBom);
            }
            else
            {
                var attr = File.GetAttributes(FreshClamConf);
                if (attr.HasFlag(FileAttributes.ReadOnly))
                    File.SetAttributes(FreshClamConf, attr & ~FileAttributes.ReadOnly);

                byte[] rawBytes = File.ReadAllBytes(FreshClamConf);
                int offset = 0;
                if (rawBytes.Length >= 3 && rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF)
                {
                    offset = 3;
                }

                string text = Encoding.UTF8.GetString(rawBytes, offset, rawBytes.Length - offset);
                bool modified = offset > 0;

                if (Regex.IsMatch(text, @"(?m)^\s*Example\s*$"))
                {
                    text = Regex.Replace(text, @"(?m)^\s*Example\s*$", "# Example (disabled)");
                    modified = true;
                }
                if (Regex.IsMatch(text, @"(?m)^\s*DatabaseDirectory\s+.*$"))
                {
                    text = Regex.Replace(text, @"(?m)^\s*DatabaseDirectory\s+.*$", "# DatabaseDirectory configured at runtime via --datadir");
                    modified = true;
                }
                if (!Regex.IsMatch(text, @"(?m)^\s*DatabaseMirror\s+database\.clamav\.net"))
                {
                    text = text.TrimEnd() + "\r\n\r\nDatabaseMirror database.clamav.net\r\n";
                    modified = true;
                }
                if (modified)
                {
                    var utf8NoBom = new UTF8Encoding(false);
                    File.WriteAllText(FreshClamConf, text, utf8NoBom);
                }
            }
        }
        catch { /* best-effort configuration */ }
    }

    public async Task<bool> UpdateDefinitionsAsync(CancellationToken ct = default)
    {
        if (!File.Exists(FreshClamPath))
            throw new FileNotFoundException("freshclam.exe not found.", FreshClamPath);

        Directory.CreateDirectory(DatabaseDir);
        EnsureFreshClamConfig();

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
        lock (_processLock)
        {
            _currentProcess = process;
        }

        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            string line = e.Data.Trim();
            DispatchEvent(() => OutputReceived?.Invoke(this, line));
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            string line = e.Data.Trim();
            DispatchEvent(() => OutputReceived?.Invoke(this, line));
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
            return false;
        }
        finally
        {
            lock (_processLock) { _currentProcess = null; }
        }

        return process.ExitCode == 0;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Quarantine Management
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to terminate any processes holding an execution lock on the given file.
    /// Essential for quarantining actively executing malware.
    /// </summary>
    public static bool TryTerminateProcessesUsingFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;

        var processes = Process.GetProcesses();
        try
        {
            string fullTarget = Path.GetFullPath(filePath);
            foreach (var proc in processes)
            {
                try
                {
                    if (proc.Id <= 4 || proc.Id == Environment.ProcessId) continue;

                    string? procPath = null;
                    try { procPath = proc.MainModule?.FileName; }
                    catch { }

                    if (!string.IsNullOrEmpty(procPath) &&
                        string.Equals(Path.GetFullPath(procPath), fullTarget, StringComparison.OrdinalIgnoreCase))
                    {
                        if (ProtectedProcesses.Contains(proc.ProcessName))
                        {
                            Debug.WriteLine($"WARNING: Prevented termination of protected process {proc.ProcessName}");
                            continue;
                        }

                        proc.Kill(entireProcessTree: true);
                        proc.WaitForExit(1000);
                    }
                }
                catch { }
            }
        }
        catch { }
        finally
        {
            foreach (var proc in processes)
            {
                proc.Dispose();
            }
        }
        return true;
    }

    public async Task<bool> QuarantineFileAsync(ThreatInfo threat)
    {
        if (!File.Exists(threat.FilePath)) return false;

        string normalizedThreatPath = Path.GetFullPath(threat.FilePath);
        foreach (var p in ProtectedPaths)
        {
            string normalizedProtectedPath = Path.GetFullPath(p);
            if (!normalizedProtectedPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                normalizedProtectedPath += Path.DirectorySeparatorChar;
            }

            if (normalizedThreatPath.StartsWith(normalizedProtectedPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedThreatPath, Path.GetFullPath(p), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Cannot quarantine protected system path: {threat.FilePath}");
            }
        }

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
        var dest = Path.Combine(QuarantineDir, entry.QuarantinedFileName);

        bool moved = false;
        try
        {
            TryTerminateProcessesUsingFile(threat.FilePath);

            var attr = File.GetAttributes(threat.FilePath);
            if (attr.HasFlag(FileAttributes.ReadOnly))
                File.SetAttributes(threat.FilePath, attr & ~FileAttributes.ReadOnly);

            File.Move(threat.FilePath, dest);
            moved = true;
        }
        catch (IOException)
        {
            // Retry once after brief yield if file was briefly locked
            await Task.Delay(100);
            try
            {
                TryTerminateProcessesUsingFile(threat.FilePath);
                File.Move(threat.FilePath, dest);
                moved = true;
            }
            catch { return false; }
        }
        catch { return false; }

        if (!moved) return false;

        await ManifestLock.WaitAsync();
        try
        {
            var manifest = await LoadManifestInternalAsync();
            manifest.Add(entry);
            await SaveManifestInternalAsync(manifest);
            return true;
        }
        catch
        {
            // Rollback moved file if manifest update fails
            try { if (File.Exists(dest)) File.Move(dest, threat.FilePath, overwrite: true); }
            catch { }
            return false;
        }
        finally
        {
            ManifestLock.Release();
        }
    }

    /// <summary>
    /// Batches multiple file quarantine actions into a single atomic manifest transaction.
    /// Dramatically reduces disk I/O and serialization overhead.
    /// </summary>
    public async Task<int> QuarantineFilesAsync(
        IEnumerable<ThreatInfo> threats,
        Action<ThreatInfo, bool>? onFileProcessed = null)
    {
        Directory.CreateDirectory(QuarantineDir);
        var newEntries = new List<QuarantineEntry>();
        var movedPairs = new List<(string Source, string Dest, ThreatInfo Threat)>();
        int successCount = 0;

        foreach (var threat in threats)
        {
            if (!File.Exists(threat.FilePath))
            {
                onFileProcessed?.Invoke(threat, false);
                continue;
            }

            var entry = new QuarantineEntry
            {
                Id               = Guid.NewGuid().ToString("N"),
                OriginalPath     = threat.FilePath,
                OriginalFileName = Path.GetFileName(threat.FilePath),
                ThreatName       = threat.ThreatName,
                QuarantinedAt    = DateTime.UtcNow,
            };
            entry.QuarantinedFileName = $"{entry.Id}.quarantine";
            var dest = Path.Combine(QuarantineDir, entry.QuarantinedFileName);

            bool moved = false;
            try
            {
                TryTerminateProcessesUsingFile(threat.FilePath);

                var attr = File.GetAttributes(threat.FilePath);
                if (attr.HasFlag(FileAttributes.ReadOnly))
                    File.SetAttributes(threat.FilePath, attr & ~FileAttributes.ReadOnly);

                File.Move(threat.FilePath, dest);
                moved = true;
            }
            catch (IOException)
            {
                await Task.Delay(50);
                try
                {
                    TryTerminateProcessesUsingFile(threat.FilePath);
                    if (File.Exists(threat.FilePath))
                    {
                        var attr = File.GetAttributes(threat.FilePath);
                        if (attr.HasFlag(FileAttributes.ReadOnly))
                            File.SetAttributes(threat.FilePath, attr & ~FileAttributes.ReadOnly);
                    }
                    File.Move(threat.FilePath, dest);
                    moved = true;
                }
                catch { }
            }
            catch { }

            if (moved)
            {
                movedPairs.Add((threat.FilePath, dest, threat));
                newEntries.Add(entry);
                successCount++;
            }
            else
            {
                onFileProcessed?.Invoke(threat, false);
            }
        }

        if (newEntries.Count > 0)
        {
            await ManifestLock.WaitAsync();
            try
            {
                var manifest = await LoadManifestInternalAsync();
                manifest.AddRange(newEntries);
                await SaveManifestInternalAsync(manifest);

                // Notify success only after manifest is committed to disk
                foreach (var item in movedPairs)
                {
                    onFileProcessed?.Invoke(item.Threat, true);
                }
            }
            catch
            {
                // Rollback moved files on manifest failure
                foreach (var (src, dst, threat) in movedPairs)
                {
                    try { if (File.Exists(dst)) File.Move(dst, src, overwrite: true); } catch { }
                    onFileProcessed?.Invoke(threat, false);
                }
                return 0;
            }
            finally
            {
                ManifestLock.Release();
            }
        }

        return successCount;
    }

    public async Task<List<QuarantineEntry>> GetQuarantineEntriesAsync()
    {
        await ManifestLock.WaitAsync();
        try
        {
            return await LoadManifestInternalAsync();
        }
        finally
        {
            ManifestLock.Release();
        }
    }

    public async Task<bool> RestoreFromQuarantineAsync(QuarantineEntry entry)
    {
        var src = Path.Combine(QuarantineDir, entry.QuarantinedFileName);
        if (!File.Exists(src))
        {
            // Missing on disk; remove ghost entry from manifest
            await ManifestLock.WaitAsync();
            try
            {
                var manifest = await LoadManifestInternalAsync();
                manifest.RemoveAll(e => e.Id == entry.Id);
                await SaveManifestInternalAsync(manifest);
            }
            finally
            {
                ManifestLock.Release();
            }
            return false;
        }

        try
        {
            var dir = Path.GetDirectoryName(entry.OriginalPath);
            if (dir is not null) Directory.CreateDirectory(dir);

            // Strip ReadOnly on source quarantine file if set
            if (File.Exists(src))
            {
                var srcAttr = File.GetAttributes(src);
                if (srcAttr.HasFlag(FileAttributes.ReadOnly))
                    File.SetAttributes(src, srcAttr & ~FileAttributes.ReadOnly);
            }

            // Strip ReadOnly on target file if it already exists so overwrite succeeds
            if (File.Exists(entry.OriginalPath))
            {
                var destAttr = File.GetAttributes(entry.OriginalPath);
                if (destAttr.HasFlag(FileAttributes.ReadOnly))
                    File.SetAttributes(entry.OriginalPath, destAttr & ~FileAttributes.ReadOnly);
            }

            File.Move(src, entry.OriginalPath, overwrite: true);
        }
        catch { return false; }

        await ManifestLock.WaitAsync();
        try
        {
            var manifest = await LoadManifestInternalAsync();
            manifest.RemoveAll(e => e.Id == entry.Id);
            await SaveManifestInternalAsync(manifest);
            return true;
        }
        finally
        {
            ManifestLock.Release();
        }
    }

    public async Task<bool> DeleteFromQuarantineAsync(QuarantineEntry entry)
    {
        try
        {
            var path = Path.Combine(QuarantineDir, entry.QuarantinedFileName);
            if (File.Exists(path))
            {
                var attr = File.GetAttributes(path);
                if (attr.HasFlag(FileAttributes.ReadOnly))
                    File.SetAttributes(path, attr & ~FileAttributes.ReadOnly);
                File.Delete(path);
            }
        }
        catch { return false; }

        await ManifestLock.WaitAsync();
        try
        {
            var manifest = await LoadManifestInternalAsync();
            manifest.RemoveAll(e => e.Id == entry.Id);
            await SaveManifestInternalAsync(manifest);
            return true;
        }
        finally
        {
            ManifestLock.Release();
        }
    }

    /// <summary>
    /// Permanently deletes all quarantined files and clears the manifest.
    /// </summary>
    public async Task<int> DeleteAllFromQuarantineAsync()
    {
        await ManifestLock.WaitAsync();
        try
        {
            var manifest = await LoadManifestInternalAsync();
            int count = 0;

            foreach (var entry in manifest)
            {
                try
                {
                    var path = Path.Combine(QuarantineDir, entry.QuarantinedFileName);
                    if (File.Exists(path))
                    {
                        var attr = File.GetAttributes(path);
                        if (attr.HasFlag(FileAttributes.ReadOnly))
                            File.SetAttributes(path, attr & ~FileAttributes.ReadOnly);
                        File.Delete(path);
                    }
                    count++;
                }
                catch { }
            }

            // Also clean up any orphaned .quarantine files on disk
            try
            {
                if (Directory.Exists(QuarantineDir))
                {
                    foreach (var file in Directory.EnumerateFiles(QuarantineDir, "*.quarantine"))
                    {
                        try
                        {
                            var attr = File.GetAttributes(file);
                            if (attr.HasFlag(FileAttributes.ReadOnly))
                                File.SetAttributes(file, attr & ~FileAttributes.ReadOnly);
                            File.Delete(file);
                        }
                        catch { }
                    }
                }
            }
            catch { }

            manifest.Clear();
            await SaveManifestInternalAsync(manifest);
            return count;
        }
        finally
        {
            ManifestLock.Release();
        }
    }

    /// <summary>
    /// Restores all quarantined files to their original paths.
    /// </summary>
    public async Task<(int Restored, int Failed)> RestoreAllFromQuarantineAsync()
    {
        await ManifestLock.WaitAsync();
        try
        {
            var manifest = await LoadManifestInternalAsync();
            var remaining = new List<QuarantineEntry>();
            int restoredCount = 0;
            int failedCount = 0;

            foreach (var entry in manifest)
            {
                var src = Path.Combine(QuarantineDir, entry.QuarantinedFileName);
                if (!File.Exists(src))
                {
                    failedCount++;
                    continue;
                }

                try
                {
                    var dir = Path.GetDirectoryName(entry.OriginalPath);
                    if (dir is not null) Directory.CreateDirectory(dir);

                    if (File.Exists(src))
                    {
                        var srcAttr = File.GetAttributes(src);
                        if (srcAttr.HasFlag(FileAttributes.ReadOnly))
                            File.SetAttributes(src, srcAttr & ~FileAttributes.ReadOnly);
                    }

                    if (File.Exists(entry.OriginalPath))
                    {
                        var destAttr = File.GetAttributes(entry.OriginalPath);
                        if (destAttr.HasFlag(FileAttributes.ReadOnly))
                            File.SetAttributes(entry.OriginalPath, destAttr & ~FileAttributes.ReadOnly);
                    }

                    File.Move(src, entry.OriginalPath, overwrite: true);
                    restoredCount++;
                }
                catch
                {
                    failedCount++;
                    remaining.Add(entry);
                }
            }

            await SaveManifestInternalAsync(remaining);
            return (restoredCount, failedCount);
        }
        finally
        {
            ManifestLock.Release();
        }
    }

    // ── Manifest I/O (Atomic & Corrupt-Resilient) ─────────────────────────────

    private static async Task<List<QuarantineEntry>> LoadManifestInternalAsync()
    {
        if (!File.Exists(ManifestPath))
            return [];

        string json = string.Empty;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                json = await File.ReadAllTextAsync(ManifestPath);
                break;
            }
            catch (IOException)
            {
                if (attempt == 2) return [];
                await Task.Delay(50);
            }
            catch { return []; }
        }

        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<QuarantineEntry>>(json) ?? [];
        }
        catch (JsonException)
        {
            try
            {
                string corruptBackup = $"{ManifestPath}.corrupt_{DateTime.UtcNow:yyyyMMddHHmmss}";
                File.Move(ManifestPath, corruptBackup, overwrite: true);
            }
            catch { }
            return [];
        }
    }

    private static async Task SaveManifestInternalAsync(List<QuarantineEntry> entries)
    {
        Directory.CreateDirectory(QuarantineDir);
        var json = JsonSerializer.Serialize(entries, JsonIndentedOptions);

        // Atomic write: write to temp file then replace
        string tempPath = Path.Combine(QuarantineDir, $"manifest_{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tempPath, json, Encoding.UTF8);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                File.Move(tempPath, ManifestPath, overwrite: true);
                return;
            }
            catch (IOException)
            {
                await Task.Delay(50);
            }
        }

        // Direct write fallback
        await File.WriteAllTextAsync(ManifestPath, json, Encoding.UTF8);
        if (File.Exists(tempPath))
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Emergency Kill
    // ─────────────────────────────────────────────────────────────────────────

    public void KillCurrentProcess()
    {
        lock (_processLock)
        {
            try
            {
                if (_currentProcess is { HasExited: false })
                    _currentProcess.Kill(entireProcessTree: true);
            }
            catch { /* already exited or disposed */ }
        }
    }
}
