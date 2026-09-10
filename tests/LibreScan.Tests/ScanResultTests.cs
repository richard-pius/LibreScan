using LibreScan.Models;
using Xunit;

namespace LibreScan.Tests;

public class ScanResultTests
{
    [Fact]
    public void ScanResult_ExitCode0_CleanState()
    {
        var result = new ScanResult
        {
            Success = true,
            MalwareDetected = false,
            ExitCode = 0,
            FilesScanned = 150,
            ThreatsFound = 0,
        };

        Assert.True(result.Success);
        Assert.False(result.MalwareDetected);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, result.ThreatsFound);
    }

    [Fact]
    public void ScanResult_ExitCode1_MalwareDetected()
    {
        var threat = new ThreatInfo
        {
            FilePath = @"C:\sample.exe",
            ThreatName = "Win.Trojan.Test",
            DetectedAt = DateTime.UtcNow,
        };

        var result = new ScanResult
        {
            Success = true,
            MalwareDetected = true,
            ExitCode = 1,
            FilesScanned = 200,
            ThreatsFound = 1,
            Threats = [threat],
        };

        Assert.True(result.Success);
        Assert.True(result.MalwareDetected);
        Assert.Equal(1, result.ThreatsFound);
        Assert.Single(result.Threats);
    }

    [Fact]
    public void ScanResult_ExitCode2WithThreats_ThreatsNotMasked()
    {
        // When ClamAV encounters a file permission error (exit code 2) but ALSO detected malware:
        // MalwareDetected must be true so the UI warns the user of threats!
        var threat = new ThreatInfo
        {
            FilePath = @"C:\malware.exe",
            ThreatName = "Win.Virus.Test",
            DetectedAt = DateTime.UtcNow,
        };

        var threats = new List<ThreatInfo> { threat };
        int exitCode = 2;

        var result = new ScanResult
        {
            Success = exitCode is 0 or 1,
            MalwareDetected = threats.Count > 0 || exitCode == 1,
            ExitCode = exitCode,
            ThreatsFound = threats.Count,
            Threats = threats,
        };

        Assert.False(result.Success);
        Assert.True(result.MalwareDetected);
        Assert.Equal(1, result.ThreatsFound);
    }

    [Fact]
    public void ScanResult_ExitCode2WithoutThreats_ScanError()
    {
        var threats = new List<ThreatInfo>();
        int exitCode = 2;

        var result = new ScanResult
        {
            Success = exitCode is 0 or 1,
            MalwareDetected = threats.Count > 0 || exitCode == 1,
            ExitCode = exitCode,
            ThreatsFound = threats.Count,
            Threats = threats,
        };

        Assert.False(result.Success);
        Assert.False(result.MalwareDetected);
        Assert.Equal(0, result.ThreatsFound);
    }
}
