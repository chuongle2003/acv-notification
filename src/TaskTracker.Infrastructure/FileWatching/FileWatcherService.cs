using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TaskTracker.Infrastructure.FileWatching;

public class FileWatcherService : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly TimeSpan _debounceDelay;

    private CancellationTokenSource? _debounceCts;
    private string? _watchedFilePath;

    public event EventHandler<string>? FileChanged;
    public event EventHandler<string>? FileDeleted;

    public FileWatcherService(TimeSpan debounceDelay = default)
    {
        _debounceDelay = debounceDelay == default ? TimeSpan.FromSeconds(2) : debounceDelay;

        _watcher = new FileSystemWatcher
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.Size
        };

        _watcher.Changed += OnFileSystemEvent;
        _watcher.Created += OnFileSystemEvent;
        _watcher.Renamed += OnFileSystemEvent;
        _watcher.Deleted += OnFileDeleted;
    }

    public void StartWatching(string filePath)
    {
        _watchedFilePath = Path.GetFullPath(filePath);

        var dir = Path.GetDirectoryName(_watchedFilePath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            throw new ArgumentException($"Directory does not exist for path: {_watchedFilePath}");
        }

        _watcher.Path = dir;
        _watcher.Filter = Path.GetFileName(_watchedFilePath);
        _watcher.EnableRaisingEvents = true;
    }

    public void StopWatching()
    {
        _watcher.EnableRaisingEvents = false;
        _debounceCts?.Cancel();
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        if (_watchedFilePath == null || !string.Equals(e.FullPath, _watchedFilePath, StringComparison.OrdinalIgnoreCase))
            return;

        // Debounce logic
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;

        Task.Delay(_debounceDelay, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
            {
                FileChanged?.Invoke(this, _watchedFilePath);
            }
        }, TaskScheduler.Default);
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        if (_watchedFilePath == null || !string.Equals(e.FullPath, _watchedFilePath, StringComparison.OrdinalIgnoreCase))
            return;

        // We don't debounce deletions to react immediately
        _debounceCts?.Cancel();
        FileDeleted?.Invoke(this, _watchedFilePath);
    }

    public void TriggerManualRefresh()
    {
        if (_watchedFilePath != null)
        {
            FileChanged?.Invoke(this, _watchedFilePath);
        }
    }

    public void Dispose()
    {
        StopWatching();
        _watcher.Dispose();
        _debounceCts?.Dispose();
    }
}
