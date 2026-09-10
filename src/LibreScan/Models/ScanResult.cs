namespace LibreScan.Models;

/// <summary>
/// Result of a ClamAV scan operation.
/// </summary>
public sealed class ScanResult
{
    /// <summary>True if the scan completed without errors (ExitCode 0 or 1).</summary>
    public bool Success { get; init; }

    /// <summary>True if ClamAV detected malware (ExitCode == 1).</summary>
    public bool MalwareDetected { get; init; }

    /// <summary>True if the user cancelled the scan.</summary>
    public bool Cancelled { get; init; }

    /// <summary>Raw ClamAV exit code. 0 = clean, 1 = threats found, 2 = error.</summary>
    public int ExitCode { get; init; }

    public int ThreatsFound { get; init; }
    public int FilesScanned { get; init; }

    public List<ThreatInfo> Threats { get; init; } = [];
    public TimeSpan Duration { get; init; }
    public string FullLog { get; init; } = string.Empty;
}

/// <summary>
/// A single threat detected during a scan.
/// </summary>
public sealed class ThreatInfo
{
    public string FilePath { get; set; } = string.Empty;
    public string ThreatName { get; set; } = string.Empty;
    public DateTime DetectedAt { get; set; }
}

/// <summary>
/// Metadata for a file that has been moved to quarantine.
/// Serialized to/from the quarantine manifest JSON.
/// </summary>
public sealed class QuarantineEntry
{
    public string Id { get; set; } = string.Empty;
    public string OriginalPath { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string QuarantinedFileName { get; set; } = string.Empty;
    public string ThreatName { get; set; } = string.Empty;
    public DateTime QuarantinedAt { get; set; }
}

/// <summary>
/// Model representing a storage drive available for scanning (C:, D:, USB flash drive, etc.).
/// </summary>
public sealed class DriveItem
{
    public string Name { get; init; } = string.Empty;
    public string VolumeLabel { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string DriveTypeDescription { get; init; } = string.Empty;
    public string CapacityDescription { get; init; } = string.Empty;
    public double FreePercentage { get; init; }
    public double UsedPercentage => Math.Clamp(100.0 - FreePercentage, 0.0, 100.0);
    public bool IsReady { get; init; }
}

/// <summary>
/// Persisted application state for scan history and preferences.
/// </summary>
public sealed class AppState
{
    public DateTime? LastScanDate { get; set; }
    public int LastScanFiles { get; set; }
    public int LastScanThreats { get; set; }
    public string LastScanType { get; set; } = string.Empty;
}

