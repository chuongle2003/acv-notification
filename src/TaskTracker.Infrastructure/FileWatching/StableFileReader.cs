using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using TaskTracker.Application;

namespace TaskTracker.Infrastructure.FileWatching;

public class StableFileReader : IStableFileReader
{
    private readonly int[] _backoffDelaysMs = { 1000, 2000, 4000, 8000, 15000, 30000 };

    public async Task<StableReadResult> ReadStableFileAsync(string sourcePath, string? lastKnownHash, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath))
        {
            return new StableReadResult(StableReadStatus.FileNotFound);
        }

        for (int attempt = 0; attempt <= _backoffDelaysMs.Length; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await IsFileStableAsync(sourcePath, cancellationToken).ConfigureAwait(false))
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
                        return new StableReadResult(StableReadStatus.InvalidFormat);
                    }

                    var newHash = ComputeSha256(tempPath);
                    if (newHash == lastKnownHash)
                    {
                        File.Delete(tempPath);
                        return new StableReadResult(StableReadStatus.Unchanged, Hash: newHash);
                    }

                    return new StableReadResult(StableReadStatus.Success, tempPath, newHash);
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

        return new StableReadResult(StableReadStatus.Timeout);
    }

    private static async Task<bool> IsFileStableAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            var initialSize = fileInfo.Length;
            var initialTime = fileInfo.LastWriteTimeUtc;

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);

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
            ReadExactly(fs, buffer);

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

    private static void ReadExactly(FileStream stream, byte[] buffer)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected end of file while reading signature.");
            }
            totalRead += read;
        }
    }
}
