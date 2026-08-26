using System;
using System.IO;
using System.Windows;
using TaskTracker.Application;
using TaskTracker.Infrastructure.FileWatching;
using TaskTracker.Windows.ViewModels;

namespace TaskTracker.Windows.Lifecycle;

public sealed class SourceMonitorService : IDisposable
{
    private readonly FileWatcherService _watcher;
    private readonly SettingsService _settingsService;
    private readonly MainViewModel _viewModel;

    public SourceMonitorService(
        FileWatcherService watcher,
        SettingsService settingsService,
        MainViewModel viewModel)
    {
        _watcher = watcher;
        _settingsService = settingsService;
        _viewModel = viewModel;
        _watcher.FileChanged += OnFileChanged;
        _watcher.FileDeleted += OnFileDeleted;
        _watcher.FileRenamed += OnFileRenamed;
    }

    public void StartFromSettings()
    {
        var path = _settingsService.Load().SourceFilePath;
        if (!string.IsNullOrWhiteSpace(path)) Restart(path);
    }

    public void Restart(string sourcePath)
    {
        try
        {
            _watcher.StartWatching(sourcePath);
        }
        catch (ArgumentException ex)
        {
            _viewModel.LastSyncStatus = $"Không theo dõi được file: {ex.Message}";
        }
    }

    public void Stop() => _watcher.StopWatching();

    public void TriggerManualRefresh() => DispatchRefresh();

    private void OnFileChanged(object? sender, string path) => DispatchRefresh();

    private void OnFileDeleted(object? sender, string path)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            _viewModel.LastSyncStatus = $"File nguồn không còn tồn tại: {Path.GetFileName(path)}");
    }

    private void OnFileRenamed(object? sender, FileRenamedEventArgs args)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var settings = _settingsService.Load();
            settings.SourceFilePath = args.NewPath;
            _settingsService.Save(settings);
            _viewModel.SourceFileName = Path.GetFileName(args.NewPath);
            Restart(args.NewPath);
            DispatchRefresh();
        });
    }

    private void DispatchRefresh()
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            _ = _viewModel.RefreshDataCommand.ExecuteAsync(null));
    }

    public void Dispose()
    {
        _watcher.FileChanged -= OnFileChanged;
        _watcher.FileDeleted -= OnFileDeleted;
        _watcher.FileRenamed -= OnFileRenamed;
        _watcher.StopWatching();
    }
}
