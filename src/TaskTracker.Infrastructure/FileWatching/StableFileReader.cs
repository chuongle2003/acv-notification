using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace TaskTracker.Infrastructure.FileWatching;

public class StableFileReader
{
    private readonly int[] _backoffDelaysMs = { 1000, 2000, 4000, 8000, 15000, 30000 };

    public async Task<StableFileResult> ReadStableFileAsync(string sourcePath, string lastKnownHash, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath))
        {
            return new StableFileResult { Status = StableFileStatus.FileNotFound };
        }

        for (int attempt = 0; attempt <= _backoffDelaysMs.Length; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsFileStable(sourcePath))
            {
                var tempPath = Path.Combine(Path.GetTempPath(), $"tasktracker_snap_{Guid.NewGuid():N}.xlsx");

                try
                {
                    // Copy with FileShare.ReadWrite to allow reading even if Excel still holds some loose handles
                    using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var tempStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await sourceStream.CopyToAsync(tempStream, cancellationToken);
                    }

                    // Validate if it's actually an XLSX (ZIP signature starts with PK - 50 4B)
                    if (!IsValidZipSignature(tempPath))
                    {
                        File.Delete(tempPath);
                        return new StableFileResult { Status = StableFileStatus.InvalidFormat };
                    }

                    var newHash = ComputeSha256(tempPath);
                    if (newHash == lastKnownHash)
                    {
                        File.Delete(tempPath);
                        return new StableFileResult { Status = StableFileStatus.Unchanged, Hash = newHash };
                    }

                    return new StableFileResult
                    {
                        Status = StableFileStatus.Success,
                        TempFilePath = tempPath,
                        Hash = newHash
                    };
                }
                catch (IOException)
                {
                    // File might be aggressively locked by Excel during the exact moment of copy.
                    // Swallow and let it retry.
                    if (File.Exists(tempPath))
                    {
                        try { File.Delete(tempPath); } catch { }
                    }
                }
            }

            if (attempt < _backoffDelaysMs.Length)
            {
                await Task.Delay(_backoffDelaysMs[attempt], cancellationToken);
            }
        }

        return new StableFileResult { Status = StableFileStatus.Timeout };
    }

    private bool IsFileStable(string path)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            var initialSize = fileInfo.Length;
            var initialTime = fileInfo.LastWriteTimeUtc;

            Thread.Sleep(500); // 500ms stabilization check

            fileInfo.Refresh();
            var finalSize = fileInfo.Length;
            var finalTime = fileInfo.LastWriteTimeUtc;

            return initialSize == finalSize && initialTime == finalTime;
        }
        catch (IOException)
        {
            // If we can't even get file info due to strict locking, it's not stable.
            return false;
        }
    }

    private bool IsValidZipSignature(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            if (fs.Length < 4) return false;

            var buffer = new byte[4];
            fs.Read(buffer, 0, 4);

            // "PK\x03\x04"
            return buffer[0] == 0x50 && buffer[1] == 0x4B && buffer[2] == 0x03 && buffer[3] == 0x04;
        }
        catch
        {
            return false;
        }
    }

    private string ComputeSha256(string path)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(path);
        var hashBytes = sha256.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}

public class StableFileResult
{
    public StableFileStatus Status { get; set; }
    public string? TempFilePath { get; set; }
    public string? Hash { get; set; }
}

public enum StableFileStatus
{
    Success,
    Unchanged,
    FileNotFound,
    InvalidFormat,
    Timeout
}
