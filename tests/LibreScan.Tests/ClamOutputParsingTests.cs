using LibreScan.Services;
using Xunit;

namespace LibreScan.Tests;

public class ClamOutputParsingTests
{
    [Fact]
    public void ParseThreatLine_StandardFormat_ExtractsCorrectPathAndName()
    {
        string line = @"C:\Users\John\Downloads\eicar.com: Win.Test.EICAR_HDB-1 FOUND";
        var threat = ClamAVService.ParseThreatLine(line);

        Assert.NotNull(threat);
        Assert.Equal(@"C:\Users\John\Downloads\eicar.com", threat.FilePath);
        Assert.Equal("Win.Test.EICAR_HDB-1", threat.ThreatName);
    }

    [Fact]
    public void ParseThreatLine_PathWithSpaces_ExtractsCleanPath()
    {
        string line = @"C:\Users\Jane Doe\My Documents\malicious file.exe: Win.Trojan.Agent FOUND";
        var threat = ClamAVService.ParseThreatLine(line);

        Assert.NotNull(threat);
        Assert.Equal(@"C:\Users\Jane Doe\My Documents\malicious file.exe", threat.FilePath);
        Assert.Equal("Win.Trojan.Agent", threat.ThreatName);
    }

    [Fact]
    public void ParseThreatLine_ArchiveNestedPath_ExtractsThreatNameAndContainer()
    {
        string line = @"C:\Downloads\archive.zip: nested/payload.exe: Win.Dropper.Generic FOUND";
        var threat = ClamAVService.ParseThreatLine(line);

        Assert.NotNull(threat);
        Assert.Equal(@"C:\Downloads\archive.zip", threat.FilePath);
        Assert.Equal("Win.Dropper.Generic", threat.ThreatName);
    }

    [Theory]
    [InlineData(@"C:\Users\John\file.txt: OK")]
    [InlineData(@"----------- SCAN SUMMARY -----------")]
    [InlineData(@"Known viruses: 3628054")]
    [InlineData(@"Scanned files: 42")]
    [InlineData(@"Infected files: 0")]
    [InlineData(@"")]
    [InlineData(@"   ")]
    public void ParseThreatLine_NonThreatLines_ReturnsNull(string line)
    {
        var threat = ClamAVService.ParseThreatLine(line);
        Assert.Null(threat);
    }

    [Fact]
    public void ParseScannedFiles_StandardSummary_ReturnsParsedCount()
    {
        string log = """
            ----------- SCAN SUMMARY -----------
            Known viruses: 3628054
            Engine version: 1.5.4
            Scanned directories: 5
            Scanned files: 1248
            Infected files: 2
            Data scanned: 15.20 MB
            """;

        int count = ClamAVService.ParseScannedFiles(log);
        Assert.Equal(1248, count);
    }

    [Fact]
    public void ParseScannedFiles_NoSummary_ReturnsZero()
    {
        string log = "Scan cancelled by user before summary was generated.";
        int count = ClamAVService.ParseScannedFiles(log);
        Assert.Equal(0, count);
    }

    [Fact]
    public void ParseInfectedFiles_StandardSummary_ReturnsInfectedCount()
    {
        string log = """
            ----------- SCAN SUMMARY -----------
            Known viruses: 3628054
            Engine version: 1.5.4
            Scanned directories: 5
            Scanned files: 1248
            Infected files: 7
            Data scanned: 15.20 MB
            """;

        int count = ClamAVService.ParseInfectedFiles(log);
        Assert.Equal(7, count);
    }

    [Fact]
    public void ParseInfectedFiles_CleanSummary_ReturnsZero()
    {
        string log = """
            ----------- SCAN SUMMARY -----------
            Known viruses: 3628054
            Engine version: 1.5.4
            Scanned directories: 1
            Scanned files: 42
            Infected files: 0
            """;

        int count = ClamAVService.ParseInfectedFiles(log);
        Assert.Equal(0, count);
    }
}
