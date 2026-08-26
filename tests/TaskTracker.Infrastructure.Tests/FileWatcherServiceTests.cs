using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TaskTracker.Infrastructure.FileWatching;
using Xunit;

namespace TaskTracker.Infrastructure.Tests;

public class FileWatcherServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _testFile;

    public FileWatcherServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"FileWatcherTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _testFile = Path.Combine(_testDir, "test.xlsx");
        File.WriteAllText(_testFile, "init");
    }

    [Fact]
    public async Task FileWatcher_DebouncesMultipleEventsIntoOne()
    {
        using var watcher = new FileWatcherService(TimeSpan.FromMilliseconds(100)); // fast debounce for test
        int invokeCount = 0;

        watcher.FileChanged += (s, path) =>
        {
            invokeCount++;
        };

        watcher.StartWatching(_testFile);

        // Simulate fast sequential saves
        File.WriteAllText(_testFile, "change 1");
        await Task.Delay(20);
        File.WriteAllText(_testFile, "change 2");
        await Task.Delay(20);
        File.WriteAllText(_testFile, "change 3");

        // Wait enough time for debounce to trigger
        await Task.Delay(200);

        Assert.Equal(1, invokeCount);
    }

    [Fact]
    public void ManualRefresh_TriggersImmediately()
    {
        using var watcher = new FileWatcherService();
        int invokeCount = 0;

        watcher.FileChanged += (s, path) =>
        {
            invokeCount++;
        };

        watcher.StartWatching(_testFile);
        watcher.TriggerManualRefresh();

        Assert.Equal(1, invokeCount);
    }

    [Fact]
    public async Task FileDeleted_TriggersImmediatelyWithoutDebounce()
    {
        using var watcher = new FileWatcherService(TimeSpan.FromMilliseconds(100));
        int deleteCount = 0;

        watcher.FileDeleted += (s, path) =>
        {
            deleteCount++;
        };

        watcher.StartWatching(_testFile);

        File.Delete(_testFile);

        // Short delay to allow OS event to propagate
        await Task.Delay(50);

        Assert.Equal(1, deleteCount);
    }

    [Fact]
    public async Task FileRenamed_ReportsOldAndNewPath()
    {
        using var watcher = new FileWatcherService(TimeSpan.FromMilliseconds(50));
        FileRenamedEventArgs? observed = null;
        watcher.FileRenamed += (_, args) => observed = args;
        watcher.StartWatching(_testFile);
        var renamed = Path.Combine(_testDir, "renamed.xlsx");

        File.Move(_testFile, renamed);
        await Task.Delay(150);

        Assert.NotNull(observed);
        Assert.Equal(Path.GetFullPath(_testFile), observed!.OldPath);
        Assert.Equal(Path.GetFullPath(renamed), observed.NewPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }
}
