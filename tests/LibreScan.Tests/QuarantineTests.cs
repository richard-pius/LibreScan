using System.IO;
using System.Text.Json;
using LibreScan.Models;
using LibreScan.Services;
using Xunit;

namespace LibreScan.Tests;

public class QuarantineTests : IDisposable
{
    private readonly string _testRoot;
    private readonly ClamAVService _service;

    public QuarantineTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"LibreScanTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
        _service = new ClamAVService();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
                Directory.Delete(_testRoot, recursive: true);
        }
        catch { }
    }

    [Fact]
    public void QuarantineEntry_Serialization_RoundTripPreservesAllFields()
    {
        var entry = new QuarantineEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            OriginalPath = @"C:\Users\Test\Downloads\trojan.exe",
            OriginalFileName = "trojan.exe",
            QuarantinedFileName = "test1234.quarantine",
            ThreatName = "Win.Trojan.Agent",
            QuarantinedAt = DateTime.UtcNow,
        };

        var list = new List<QuarantineEntry> { entry };
        string json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
        var deserialized = JsonSerializer.Deserialize<List<QuarantineEntry>>(json);

        Assert.NotNull(deserialized);
        Assert.Single(deserialized);
        Assert.Equal(entry.Id, deserialized[0].Id);
        Assert.Equal(entry.OriginalPath, deserialized[0].OriginalPath);
        Assert.Equal(entry.OriginalFileName, deserialized[0].OriginalFileName);
        Assert.Equal(entry.ThreatName, deserialized[0].ThreatName);
    }

    [Fact]
    public async Task QuarantineFile_ValidFile_MovesFileAndRecordsEntry()
    {
        string sampleFile = Path.Combine(_testRoot, "malware_sample.exe");
        await File.WriteAllTextAsync(sampleFile, "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR");

        var threat = new ThreatInfo
        {
            FilePath = sampleFile,
            ThreatName = "Win.Test.EICAR",
            DetectedAt = DateTime.UtcNow,
        };

        bool success = await _service.QuarantineFileAsync(threat);

        Assert.True(success);
        Assert.False(File.Exists(sampleFile), "Original file must no longer exist at original path");

        var entries = await _service.GetQuarantineEntriesAsync();
        var matching = entries.FirstOrDefault(e => e.OriginalPath == sampleFile);
        Assert.NotNull(matching);
        Assert.Equal("Win.Test.EICAR", matching.ThreatName);
        Assert.Equal("malware_sample.exe", matching.OriginalFileName);

        // Clean up test entry from quarantine
        await _service.DeleteFromQuarantineAsync(matching);
    }

    [Fact]
    public async Task RestoreFile_QuarantinedFile_RestoresOriginalAndCleansManifest()
    {
        string sampleFile = Path.Combine(_testRoot, "restore_test.exe");
        await File.WriteAllTextAsync(sampleFile, "Restoration content payload");

        var threat = new ThreatInfo
        {
            FilePath = sampleFile,
            ThreatName = "Win.Test.Sample",
            DetectedAt = DateTime.UtcNow,
        };

        bool quarantined = await _service.QuarantineFileAsync(threat);
        Assert.True(quarantined);

        var entries = await _service.GetQuarantineEntriesAsync();
        var entry = entries.First(e => e.OriginalPath == sampleFile);

        bool restored = await _service.RestoreFromQuarantineAsync(entry);
        Assert.True(restored);
        Assert.True(File.Exists(sampleFile), "Restored file must exist at original path");

        string content = await File.ReadAllTextAsync(sampleFile);
        Assert.Equal("Restoration content payload", content);

        var afterRestoreEntries = await _service.GetQuarantineEntriesAsync();
        Assert.DoesNotContain(afterRestoreEntries, e => e.Id == entry.Id);
    }

    [Fact]
    public async Task QuarantineFile_NonExistentFile_ReturnsFalseGracefully()
    {
        var threat = new ThreatInfo
        {
            FilePath = Path.Combine(_testRoot, "ghost_file.exe"),
            ThreatName = "Win.NonExistent",
            DetectedAt = DateTime.UtcNow,
        };

        bool result = await _service.QuarantineFileAsync(threat);
        Assert.False(result);
    }

    [Fact]
    public async Task ConcurrentQuarantine_MultipleTasks_MaintainsManifestIntegrity()
    {
        var tasks = new List<Task<bool>>();
        var files = new List<string>();

        for (int i = 0; i < 5; i++)
        {
            string filePath = Path.Combine(_testRoot, $"concurrent_{i}.exe");
            await File.WriteAllTextAsync(filePath, $"Payload {i}");
            files.Add(filePath);

            var threat = new ThreatInfo
            {
                FilePath = filePath,
                ThreatName = $"Threat.Concurrent.{i}",
                DetectedAt = DateTime.UtcNow,
            };

            tasks.Add(_service.QuarantineFileAsync(threat));
        }

        var results = await Task.WhenAll(tasks);
        Assert.All(results, Assert.True);

        var entries = await _service.GetQuarantineEntriesAsync();
        foreach (var file in files)
        {
            var match = entries.FirstOrDefault(e => e.OriginalPath == file);
            Assert.NotNull(match);
            await _service.DeleteFromQuarantineAsync(match);
        }
    }

    [Fact]
    public async Task QuarantineFiles_Batch_MovesAllAndRecordsInSingleManifestUpdate()
    {
        var threats = new List<ThreatInfo>();
        var files = new List<string>();

        for (int i = 0; i < 3; i++)
        {
            string filePath = Path.Combine(_testRoot, $"batch_{i}.exe");
            await File.WriteAllTextAsync(filePath, $"Batch Content {i}");
            files.Add(filePath);

            threats.Add(new ThreatInfo
            {
                FilePath = filePath,
                ThreatName = $"Threat.Batch.{i}",
                DetectedAt = DateTime.UtcNow,
            });
        }

        var processed = new List<(ThreatInfo Threat, bool Success)>();
        int count = await _service.QuarantineFilesAsync(threats, (t, s) => processed.Add((t, s)));

        Assert.Equal(3, count);
        Assert.Equal(3, processed.Count);
        Assert.All(processed, p => Assert.True(p.Success));

        var entries = await _service.GetQuarantineEntriesAsync();
        foreach (var file in files)
        {
            Assert.False(File.Exists(file), $"Original file should be moved: {file}");
            var match = entries.FirstOrDefault(e => e.OriginalPath == file);
            Assert.NotNull(match);
            await _service.DeleteFromQuarantineAsync(match);
        }
    }

    [Fact]
    public async Task RestoreFile_OverExistingReadOnlyFile_Succeeds()
    {
        string sampleFile = Path.Combine(_testRoot, "readonly_restore_test.exe");
        await File.WriteAllTextAsync(sampleFile, "Original Version");

        var threat = new ThreatInfo
        {
            FilePath = sampleFile,
            ThreatName = "Threat.ReadOnlyTest",
            DetectedAt = DateTime.UtcNow,
        };

        bool quarantined = await _service.QuarantineFileAsync(threat);
        Assert.True(quarantined);

        // Recreate destination file and mark it ReadOnly
        await File.WriteAllTextAsync(sampleFile, "Dummy Conflict File");
        File.SetAttributes(sampleFile, FileAttributes.ReadOnly);

        var entries = await _service.GetQuarantineEntriesAsync();
        var entry = entries.First(e => e.OriginalPath == sampleFile);

        bool restored = await _service.RestoreFromQuarantineAsync(entry);
        Assert.True(restored);

        string restoredContent = await File.ReadAllTextAsync(sampleFile);
        Assert.Equal("Original Version", restoredContent);

        // Cleanup
        File.SetAttributes(sampleFile, FileAttributes.Normal);
        File.Delete(sampleFile);
    }

    [Fact]
    public async Task DeleteFile_ReadOnlyQuarantinedFile_Succeeds()
    {
        string sampleFile = Path.Combine(_testRoot, "readonly_delete_test.exe");
        await File.WriteAllTextAsync(sampleFile, "Delete Me");

        var threat = new ThreatInfo
        {
            FilePath = sampleFile,
            ThreatName = "Threat.ReadOnlyDelete",
            DetectedAt = DateTime.UtcNow,
        };

        bool quarantined = await _service.QuarantineFileAsync(threat);
        Assert.True(quarantined);

        var entries = await _service.GetQuarantineEntriesAsync();
        var entry = entries.First(e => e.OriginalPath == sampleFile);

        // Mark the quarantined file on disk as ReadOnly
        string quarantinedPath = Path.Combine(ClamAVService.QuarantineDir, entry.QuarantinedFileName);
        File.SetAttributes(quarantinedPath, FileAttributes.ReadOnly);

        bool deleted = await _service.DeleteFromQuarantineAsync(entry);
        Assert.True(deleted);
        Assert.False(File.Exists(quarantinedPath));
    }
}
