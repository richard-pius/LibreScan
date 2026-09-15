using System.IO;
using System.Text.Json;
using LibreScan.Models;
using LibreScan.Services;
using LibreScan.ViewModels;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace LibreScan.Tests;

public class NewFeaturesTests : IDisposable
{
    private readonly string _testRoot;
    private readonly ClamAVService _service;

    public NewFeaturesTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"LibreScanNewFeatures_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
        _service = new ClamAVService();
    }

    public void Dispose()
    {
        try
        {
            _service.DeleteAllFromQuarantineAsync().GetAwaiter().GetResult();
        }
        catch { }

        try
        {
            if (Directory.Exists(_testRoot))
                Directory.Delete(_testRoot, recursive: true);
        }
        catch { }
    }

    [Fact]
    public void AppState_Serialization_RoundTripPreservesData()
    {
        var original = new AppState
        {
            LastScanDate = new DateTime(2026, 9, 10, 6, 30, 0, DateTimeKind.Utc),
            LastScanFiles = 12450,
            LastScanThreats = 0,
            LastScanType = "Quick Scan"
        };

        string json = JsonSerializer.Serialize(original, new JsonSerializerOptions { WriteIndented = true });
        var restored = JsonSerializer.Deserialize<AppState>(json);

        Assert.NotNull(restored);
        Assert.Equal(original.LastScanDate, restored.LastScanDate);
        Assert.Equal(original.LastScanFiles, restored.LastScanFiles);
        Assert.Equal(original.LastScanThreats, restored.LastScanThreats);
        Assert.Equal(original.LastScanType, restored.LastScanType);
    }

    [Fact]
    public async Task DeleteAllFromQuarantine_RemovesAllEntriesAndFiles()
    {
        await _service.DeleteAllFromQuarantineAsync();

        string sample1 = Path.Combine(_testRoot, "malware1.exe");
        string sample2 = Path.Combine(_testRoot, "malware2.exe");
        await File.WriteAllTextAsync(sample1, "MalwarePayload1");
        await File.WriteAllTextAsync(sample2, "MalwarePayload2");

        var threat1 = new ThreatInfo { FilePath = sample1, ThreatName = "Threat.One", DetectedAt = DateTime.UtcNow };
        var threat2 = new ThreatInfo { FilePath = sample2, ThreatName = "Threat.Two", DetectedAt = DateTime.UtcNow };

        await _service.QuarantineFileAsync(threat1);
        await _service.QuarantineFileAsync(threat2);

        var entriesBefore = await _service.GetQuarantineEntriesAsync();
        Assert.True(entriesBefore.Count >= 2);

        int deletedCount = await _service.DeleteAllFromQuarantineAsync();
        Assert.True(deletedCount >= 2);

        var entriesAfter = await _service.GetQuarantineEntriesAsync();
        Assert.Empty(entriesAfter);
    }

    [Fact]
    public async Task RestoreAllFromQuarantine_RestoresAllFilesToOriginalLocations()
    {
        await _service.DeleteAllFromQuarantineAsync();

        string sample1 = Path.Combine(_testRoot, "restore1.exe");
        string sample2 = Path.Combine(_testRoot, "restore2.exe");
        await File.WriteAllTextAsync(sample1, "RestorePayload1");
        await File.WriteAllTextAsync(sample2, "RestorePayload2");

        var threat1 = new ThreatInfo { FilePath = sample1, ThreatName = "Threat.RestoreOne", DetectedAt = DateTime.UtcNow };
        var threat2 = new ThreatInfo { FilePath = sample2, ThreatName = "Threat.RestoreTwo", DetectedAt = DateTime.UtcNow };

        await _service.QuarantineFileAsync(threat1);
        await _service.QuarantineFileAsync(threat2);

        Assert.False(File.Exists(sample1));
        Assert.False(File.Exists(sample2));

        var (restored, failed) = await _service.RestoreAllFromQuarantineAsync();
        Assert.True(restored >= 2);
        Assert.Equal(0, failed);

        Assert.True(File.Exists(sample1));
        Assert.True(File.Exists(sample2));
        Assert.Equal("RestorePayload1", await File.ReadAllTextAsync(sample1));
        Assert.Equal("RestorePayload2", await File.ReadAllTextAsync(sample2));

        var entriesAfter = await _service.GetQuarantineEntriesAsync();
        Assert.Empty(entriesAfter);
    }

    [Fact]
    public void App_GetTargetPath_ParsesCorrectly()
    {
        // 1. Direct path
        string? p1 = App.GetTargetPath(["C:\\test\\sample.exe"]);
        Assert.Equal("C:\\test\\sample.exe", p1);

        // 2. /scan parameter
        string? p2 = App.GetTargetPath(["/scan", "D:\\Documents\\Work"]);
        Assert.Equal("D:\\Documents\\Work", p2);

        // 3. /scan: prefix
        string? p3 = App.GetTargetPath(["/scan:E:\\Backup"]);
        Assert.Equal("E:\\Backup", p3);

        // 4. --startup ignored
        string? p4 = App.GetTargetPath(["--startup"]);
        Assert.Null(p4);

        // 5. --startup with target
        string? p5 = App.GetTargetPath(["--startup", "C:\\target.dll"]);
        Assert.Equal("C:\\target.dll", p5);

        // 6. Quoted paths stripped properly
        string? p6 = App.GetTargetPath(["\"C:\\Program Files\\app.exe\""]);
        Assert.Equal("C:\\Program Files\\app.exe", p6);
    }

    [Fact]
    public void MainViewModel_DismissThreat_RemovesThreatFromList()
    {
        var vm = new MainViewModel();

        var threat1 = new ThreatInfo { FilePath = @"C:\sample1.exe", ThreatName = "Win.Test.1", DetectedAt = DateTime.UtcNow };
        var threat2 = new ThreatInfo { FilePath = @"C:\sample2.exe", ThreatName = "Win.Test.2", DetectedAt = DateTime.UtcNow };

        vm.DetectedThreats.Add(threat1);
        vm.DetectedThreats.Add(threat2);

        Assert.Equal(2, vm.DetectedThreats.Count);

        vm.DismissThreatCommand.Execute(threat1);
        Assert.Single(vm.DetectedThreats);
        Assert.Equal(threat2, vm.DetectedThreats[0]);

        vm.DismissThreatCommand.Execute(threat2);
        Assert.Empty(vm.DetectedThreats);
    }

    [Fact]
    public void MainViewModel_ClearLog_ClearsLogEntries()
    {
        var vm = new MainViewModel();
        vm.LogEntries.Add("Log 1");
        vm.LogEntries.Add("Log 2");
        Assert.Equal(2, vm.LogEntries.Count);

        vm.ClearLogCommand.Execute(null);
        Assert.Empty(vm.LogEntries);
    }

    [Fact]
    public void TargetDisplayName_ResolvesRootDrivesAndNestedPaths()
    {
        // Root drive with backslash
        Assert.Equal("C:\\", MainViewModel.GetTargetDisplayName("C:\\"));

        // Root drive without backslash
        Assert.Equal("D:", MainViewModel.GetTargetDisplayName("D:"));

        // Nested folder
        Assert.Equal("Downloads", MainViewModel.GetTargetDisplayName(@"C:\Users\User\Downloads"));
        Assert.Equal("Downloads", MainViewModel.GetTargetDisplayName(@"C:\Users\User\Downloads\"));

        // File path
        Assert.Equal("payload.exe", MainViewModel.GetTargetDisplayName(@"C:\Temp\payload.exe"));

        // Quoted path
        Assert.Equal("payload.exe", MainViewModel.GetTargetDisplayName("\"C:\\Temp\\payload.exe\""));

        // Whitespace or empty fallback
        Assert.Equal("Item", MainViewModel.GetTargetDisplayName(""));
        Assert.Equal("Item", MainViewModel.GetTargetDisplayName("   "));
    }

    [Fact]
    public async Task RunScanAsync_EmptyOrWhitespaceTargets_ReturnsCleanResultImmediately()
    {
        var r1 = await _service.RunScanAsync([]);
        Assert.True(r1.Success);
        Assert.Equal(0, r1.FilesScanned);
        Assert.Equal(0, r1.ThreatsFound);

        var r2 = await _service.RunScanAsync(["", "   "]);
        Assert.True(r2.Success);
        Assert.Equal(0, r2.FilesScanned);
        Assert.Equal(0, r2.ThreatsFound);
    }

    [Fact]
    public async Task QuarantineFilesAsync_ReadOnlyFiles_QuarantinesSuccessfully()
    {
        await _service.DeleteAllFromQuarantineAsync();

        string sampleFile = Path.Combine(_testRoot, "readonly_malware.exe");
        await File.WriteAllTextAsync(sampleFile, "ReadOnlyMalware");
        File.SetAttributes(sampleFile, FileAttributes.ReadOnly);

        var threat = new ThreatInfo { FilePath = sampleFile, ThreatName = "Threat.ReadOnly", DetectedAt = DateTime.UtcNow };

        bool callbackCalled = false;
        int count = await _service.QuarantineFilesAsync([threat], (t, ok) =>
        {
            if (t.FilePath == sampleFile && ok)
                callbackCalled = true;
        });

        Assert.Equal(1, count);
        Assert.True(callbackCalled);
        Assert.False(File.Exists(sampleFile));

        var entries = await _service.GetQuarantineEntriesAsync();
        Assert.Contains(entries, e => e.OriginalPath == sampleFile);
    }

    [Fact]
    public async Task RestoreFromQuarantineAsync_ReadOnlyQuarantinedFile_RestoresSuccessfully()
    {
        await _service.DeleteAllFromQuarantineAsync();

        string sampleFile = Path.Combine(_testRoot, "readonly_restore_src.exe");
        await File.WriteAllTextAsync(sampleFile, "RestorePayload");

        var threat = new ThreatInfo { FilePath = sampleFile, ThreatName = "Threat.RestoreReadOnly", DetectedAt = DateTime.UtcNow };
        await _service.QuarantineFileAsync(threat);

        var entries = await _service.GetQuarantineEntriesAsync();
        var entry = entries.First(e => e.OriginalPath == sampleFile);

        // Mark the quarantined file in QuarantineDir as ReadOnly
        string qFile = Path.Combine(ClamAVService.QuarantineDir, entry.QuarantinedFileName);
        File.SetAttributes(qFile, FileAttributes.ReadOnly);

        bool restored = await _service.RestoreFromQuarantineAsync(entry);
        Assert.True(restored);
        Assert.True(File.Exists(sampleFile));
        Assert.Equal("RestorePayload", await File.ReadAllTextAsync(sampleFile));

        // Cleanup
        if (File.Exists(sampleFile))
        {
            File.SetAttributes(sampleFile, FileAttributes.Normal);
            File.Delete(sampleFile);
        }
    }

    [Fact]
    public async Task DeleteAllFromQuarantineAsync_CleansOrphanedQuarantineFiles()
    {
        await _service.DeleteAllFromQuarantineAsync();

        // Create an orphaned .quarantine file not in manifest
        Directory.CreateDirectory(ClamAVService.QuarantineDir);
        string orphanPath = Path.Combine(ClamAVService.QuarantineDir, "orphan_test.quarantine");
        await File.WriteAllTextAsync(orphanPath, "Ghost quarantined file");

        int deleted = await _service.DeleteAllFromQuarantineAsync();
        Assert.False(File.Exists(orphanPath), "Orphaned quarantine files must be deleted");
    }

    [Fact]
    public void RelayCommand_RaiseCanExecuteChanged_DoesNotThrow()
    {
        var cmd = new Helpers.RelayCommand(_ => { });
        // Should execute smoothly without throwing exceptions
        cmd.RaiseCanExecuteChanged();
    }

    [Fact]
    public async Task MainViewModel_EmptyQuarantineAsync_ClearsQuarantine()
    {
        var vm = new MainViewModel();

        string sample = Path.Combine(_testRoot, "vm_quarantine_test.exe");
        await File.WriteAllTextAsync(sample, "VM Quarantine Test");
        var threat = new ThreatInfo { FilePath = sample, ThreatName = "Threat.VM", DetectedAt = DateTime.UtcNow };
        await _service.QuarantineFileAsync(threat);

        var entries = await _service.GetQuarantineEntriesAsync();
        vm.QuarantineEntries.Clear();
        foreach (var e in entries) vm.QuarantineEntries.Add(e);

        Assert.NotEmpty(vm.QuarantineEntries);

        await vm.EmptyQuarantineAsync(skipConfirmation: true);

        Assert.Empty(vm.QuarantineEntries);
    }

    [Fact]
    public async Task MainViewModel_RestoreAllQuarantineAsync_RestoresAllFiles()
    {
        var vm = new MainViewModel();

        string sample = Path.Combine(_testRoot, "vm_restore_test.exe");
        await File.WriteAllTextAsync(sample, "VM Restore Test");
        var threat = new ThreatInfo { FilePath = sample, ThreatName = "Threat.VMRestore", DetectedAt = DateTime.UtcNow };
        await _service.QuarantineFileAsync(threat);

        Assert.False(File.Exists(sample));

        var entries = await _service.GetQuarantineEntriesAsync();
        vm.QuarantineEntries.Clear();
        foreach (var e in entries) vm.QuarantineEntries.Add(e);

        Assert.NotEmpty(vm.QuarantineEntries);

        await vm.RestoreAllQuarantineAsync(skipConfirmation: true);

        Assert.Empty(vm.QuarantineEntries);
        Assert.True(File.Exists(sample));
        Assert.Equal("VM Restore Test", await File.ReadAllTextAsync(sample));
    }

    [Fact]
    public async Task MainViewModel_ScanPathFromExternalAsync_NonExistentPath_LogsGracefully()
    {
        var vm = new MainViewModel();
        string ghostPath = @"C:\NonExistent_Dir_12345\ghost.exe";

        await vm.ScanPathFromExternalAsync(ghostPath);

        Assert.Contains(vm.LogEntries, l => l.Contains("External target not found"));
    }

    [Fact]
    public void MainViewModel_DriveSelector_OpensAndClosesCorrectly()
    {
        var vm = new MainViewModel();
        Assert.False(vm.IsDriveSelectorOpen);

        vm.OpenDriveSelectorCommand.Execute(null);
        Assert.True(vm.IsDriveSelectorOpen);
        Assert.NotEmpty(vm.AvailableDrives);

        vm.CloseDriveSelectorCommand.Execute(null);
        Assert.False(vm.IsDriveSelectorOpen);
    }

    [Fact]
    public void ClamAVService_TryTerminateProcessesUsingFile_HandlesEdgeCasesGracefully()
    {
        // Must handle null, empty, whitespace, and ghost paths without throwing exceptions
        ClamAVService.TryTerminateProcessesUsingFile("");
        ClamAVService.TryTerminateProcessesUsingFile("   ");
        ClamAVService.TryTerminateProcessesUsingFile(@"C:\NonExistent_123\ghost.exe");
    }

    [Fact]
    public async Task MainViewModel_QuarantineSingleAsync_NonExistentFile_LogsFailed()
    {
        var vm = new MainViewModel();
        var threat = new ThreatInfo
        {
            FilePath = @"C:\NonExistent_Ghost_File.exe",
            ThreatName = "Ghost.Threat",
            DetectedAt = DateTime.UtcNow,
        };

        await vm.QuarantineSingleAsync(threat);

        Assert.Contains(vm.LogEntries, l => l.Contains("FAILED to quarantine"));
    }

    [Fact]
    public void MainViewModel_LogBatchTrimming_TrimsWhenThresholdExceeded()
    {
        var vm = new MainViewModel();

        // Push 2250 items through the log mechanism by simulating OutputReceived
        var method = typeof(MainViewModel).GetMethod("Log", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);

        for (int i = 0; i < 2210; i++)
        {
            method.Invoke(vm, [$"Test Log Line {i}"]);
        }

        // Should have trimmed down to 2000 + recent excess
        Assert.True(vm.LogEntries.Count <= 2200);
        Assert.True(vm.LogEntries.Count >= 2000);
    }
}
