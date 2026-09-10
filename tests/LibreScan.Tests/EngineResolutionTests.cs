using System.IO;
using LibreScan.Services;
using Xunit;

namespace LibreScan.Tests;

public class EngineResolutionTests
{
    [Fact]
    public void IsEngineAvailable_InWorkspace_ResolvesToTrue()
    {
        // With directory resolution searching up parent directories in development/test mode,
        // clamav_bin/clamscan.exe should be found.
        bool available = ClamAVService.IsEngineAvailable();
        Assert.True(available, $"Engine clamscan.exe should be located. Resolved path: {ClamAVService.ClamScanPath}");
    }

    [Fact]
    public void GetQuickScanTargets_ReturnsNonEmptyAndValidDirectories()
    {
        var targets = ClamAVService.GetQuickScanTargets();
        Assert.NotEmpty(targets);

        foreach (var target in targets)
        {
            Assert.True(Directory.Exists(target), $"Target directory should exist: {target}");
        }
    }

    [Fact]
    public void GetFullScanTargets_ReturnsReadyDrives()
    {
        var targets = ClamAVService.GetFullScanTargets();
        Assert.NotEmpty(targets);

        foreach (var driveRoot in targets)
        {
            Assert.True(Directory.Exists(driveRoot), $"Drive root should exist: {driveRoot}");
        }
    }
}
