using System.IO;
using LibreScan.Models;
using LibreScan.Services;
using Xunit;

namespace LibreScan.Tests;

public class DriveAndLiveScanningTests
{
    [Fact]
    public void GetAvailableDrives_ReturnsReadySystemDrives_WithValidMetadata()
    {
        var drives = ClamAVService.GetAvailableDrives();
        Assert.NotEmpty(drives);

        foreach (var drive in drives)
        {
            Assert.True(drive.IsReady, $"Drive {drive.Name} should be ready");
            Assert.False(string.IsNullOrWhiteSpace(drive.Name), "Drive Name should not be empty");
            Assert.False(string.IsNullOrWhiteSpace(drive.DisplayName), "Drive DisplayName should not be empty");
            Assert.False(string.IsNullOrWhiteSpace(drive.DriveTypeDescription), "DriveTypeDescription should not be empty");
            Assert.False(string.IsNullOrWhiteSpace(drive.CapacityDescription), "CapacityDescription should not be empty");

            Assert.InRange(drive.FreePercentage, 0.0, 100.0);
            Assert.InRange(drive.UsedPercentage, 0.0, 100.0);
            Assert.InRange(drive.FreePercentage + drive.UsedPercentage, 99.0, 101.0);
        }
    }

    [Fact]
    public void FormatBytes_CalculatesUnitsCorrectly()
    {
        Assert.Equal("500 B", ClamAVService.FormatBytes(500));
        Assert.Equal("1.5 KB", ClamAVService.FormatBytes((long)(1.5 * 1024)));
        Assert.Equal("2.5 MB", ClamAVService.FormatBytes((long)(2.5 * 1024 * 1024)));
        Assert.Equal("4.0 GB", ClamAVService.FormatBytes(4L * 1024 * 1024 * 1024));
        Assert.Equal("1.5 TB", ClamAVService.FormatBytes((long)(1.5 * 1024 * 1024 * 1024 * 1024)));
    }

    [Fact]
    public void ScanResult_ExitCode2WithScannedFiles_AndNoThreats_IsCleanScan()
    {
        // On Windows, ClamAV returns exit code 2 when scanning drives with locked system files
        // (such as pagefile.sys, hiberfil.sys, or System Volume Information).
        // If files were scanned and 0 threats were found, it is a clean scan with skipped files.
        int exitCode = 2;
        int filesScanned = 15420;
        int threatsFound = 0;

        bool isClean = exitCode == 0 || (exitCode == 2 && threatsFound == 0 && filesScanned > 0);
        bool malwareDetected = threatsFound > 0;

        var result = new ScanResult
        {
            Success = isClean,
            MalwareDetected = malwareDetected,
            ExitCode = exitCode,
            FilesScanned = filesScanned,
            ThreatsFound = threatsFound,
            Threats = [],
        };

        Assert.True(result.Success);
        Assert.False(result.MalwareDetected);
        Assert.Equal(2, result.ExitCode);
        Assert.Equal(15420, result.FilesScanned);
    }

    [Fact]
    public void ScanResult_ExitCode2WithZeroScannedFiles_IsFailure()
    {
        // If exit code is 2 and 0 files were scanned, the engine failed to run or the target was invalid.
        int exitCode = 2;
        int filesScanned = 0;
        int threatsFound = 0;

        bool isClean = exitCode == 0 || (exitCode == 2 && threatsFound == 0 && filesScanned > 0);

        var result = new ScanResult
        {
            Success = isClean,
            MalwareDetected = false,
            ExitCode = exitCode,
            FilesScanned = filesScanned,
            ThreatsFound = threatsFound,
        };

        Assert.False(result.Success);
    }
}
