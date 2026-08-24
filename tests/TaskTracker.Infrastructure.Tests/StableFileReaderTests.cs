using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TaskTracker.Infrastructure.FileWatching;
using Xunit;

namespace TaskTracker.Infrastructure.Tests;

public class StableFileReaderTests : IDisposable
{
    private readonly string _testDir;
    private readonly StableFileReader _reader;

    public StableFileReaderTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"StableFileReaderTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _reader = new StableFileReader();
    }

    private void CreateValidXlsxFile(string path)
    {
        // Minimal valid ZIP file signature (PK\x03\x04)
        File.WriteAllBytes(path, new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00 });
    }

    [Fact]
    public async Task ReadStableFileAsync_FileNotFound_ReturnsCorrectStatus()
    {
        var result = await _reader.ReadStableFileAsync(Path.Combine(_testDir, "missing.xlsx"), "");
        Assert.Equal(StableFileStatus.FileNotFound, result.Status);
    }

    [Fact]
    public async Task ReadStableFileAsync_InvalidFormat_ReturnsCorrectStatus()
    {
        var sourcePath = Path.Combine(_testDir, "invalid.xlsx");
        File.WriteAllText(sourcePath, "This is not a zip file");

        var result = await _reader.ReadStableFileAsync(sourcePath, "");

        Assert.Equal(StableFileStatus.InvalidFormat, result.Status);
    }

    [Fact]
    public async Task ReadStableFileAsync_Success_ReturnsTempPathAndHash()
    {
        var sourcePath = Path.Combine(_testDir, "valid.xlsx");
        CreateValidXlsxFile(sourcePath);

        var result = await _reader.ReadStableFileAsync(sourcePath, "old_hash");

        Assert.Equal(StableFileStatus.Success, result.Status);
        Assert.NotNull(result.TempFilePath);
        Assert.True(File.Exists(result.TempFilePath));
        Assert.NotNull(result.Hash);

        // Cleanup
        if (File.Exists(result.TempFilePath))
        {
            File.Delete(result.TempFilePath);
        }
    }

    [Fact]
    public async Task ReadStableFileAsync_HashUnchanged_ReturnsUnchanged()
    {
        var sourcePath = Path.Combine(_testDir, "valid.xlsx");
        CreateValidXlsxFile(sourcePath);

        // First read to get hash
        var result1 = await _reader.ReadStableFileAsync(sourcePath, "");
        var hash = result1.Hash;
        if (File.Exists(result1.TempFilePath)) File.Delete(result1.TempFilePath);

        // Second read with same hash
        var result2 = await _reader.ReadStableFileAsync(sourcePath, hash!);

        Assert.Equal(StableFileStatus.Unchanged, result2.Status);
        Assert.Null(result2.TempFilePath); // Temp file should be deleted automatically
        Assert.Equal(hash, result2.Hash);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }
}
