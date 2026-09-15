using System.IO;
using LibreScan.Services;
using Xunit;

namespace LibreScan.Tests;

public class EngineResolutionTests
{
    [Fact]
    public void IsEngineAvailable_InWorkspace_ResolvesToTrue()
    {
        // Verifies IsEngineAvailable accurately reflects whether clamscan.exe is on disk
        bool expected = File.Exists(ClamAVService.ClamScanPath);
        Assert.Equal(expected, ClamAVService.IsEngineAvailable());
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
