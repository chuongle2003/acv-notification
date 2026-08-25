using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskTracker.Application;
using TaskTracker.Domain;
using TaskStatus = TaskTracker.Domain.TaskStatus;
using TaskTracker.Infrastructure.Excel;
using TaskTracker.Infrastructure.Persistence;

namespace TaskTracker.Windows.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ImportWorkbookUseCase _importUseCase;
    private readonly SqliteTaskRepository _repository;
    private readonly SettingsService _settingsService;
    private readonly ExcelReader _excelReader;
    private readonly IDbConnectionFactory _connectionFactory;

    [ObservableProperty]
    private string _sourceFileName = "Chưa chọn file";

    [ObservableProperty]
    private string _lastSyncStatus = "Chưa đồng bộ";

    [ObservableProperty]
    private int _overdueCount = 0;

    [ObservableProperty]
    private int _dueTodayCount = 0;

    [ObservableProperty]
    private int _dueSoonCount = 0;

    [ObservableProperty]
    private int _needsReviewCount = 0;

    [ObservableProperty]
    private bool _isLoading = false;

    // Filters
    [ObservableProperty]
    private string _searchText = "";

    partial void OnSearchTextChanged(string value)
    {
        _tasksView?.Refresh();
    }

    [ObservableProperty]
    private bool _showUnreadOnly = false;

    partial void OnShowUnreadOnlyChanged(bool value)
    {
        _tasksView?.Refresh();
    }

    [ObservableProperty]
    private TaskRow? _selectedTask;

    public ObservableCollection<TaskRow> Tasks { get; } = new();

    private ICollectionView _tasksView;
    public ICollectionView TasksView => _tasksView;

    public MainViewModel(ImportWorkbookUseCase importUseCase, SqliteTaskRepository repository,
        SettingsService settingsService, ExcelReader excelReader, IDbConnectionFactory connectionFactory)
    {
        _importUseCase = importUseCase;
        _repository = repository;
        _settingsService = settingsService;
        _excelReader = excelReader;
        _connectionFactory = connectionFactory;

        _tasksView = CollectionViewSource.GetDefaultView(Tasks);
        _tasksView.Filter = FilterTask;

        // Sorting
        _tasksView.SortDescriptions.Add(new SortDescription(nameof(TaskRow.CurrentStatus), ListSortDirection.Ascending));
        _tasksView.SortDescriptions.Add(new SortDescription(nameof(TaskRow.SheetWeekNumber), ListSortDirection.Descending));
        _tasksView.SortDescriptions.Add(new SortDescription(nameof(TaskRow.SourceRowNumber), ListSortDirection.Ascending));

        // Reflect the persisted source path on startup.
        var savedPath = _settingsService.Load().SourceFilePath;
        if (!string.IsNullOrEmpty(savedPath))
        {
            SourceFileName = Path.GetFileName(savedPath);
        }
    }

    private bool FilterTask(object obj)
    {
        if (obj is not TaskRow task) return false;

        // Search text
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var st = SearchText.ToLowerInvariant();
            bool matchDoc = task.DocumentNumber?.ToLowerInvariant().Contains(st) == true;
            bool matchContent = task.TaskContent?.ToLowerInvariant().Contains(st) == true;
            if (!matchDoc && !matchContent) return false;
        }

        // Show Unread Only (Placeholder logic: we assume it's read if we had a NotificationState... for MVP UI logic we will tie it to a property)
        // Wait, the specification says "Dòng đang cần chú ý có checkbox Đã xem trong UI"
        // In TaskRow we don't have IsAcknowledged directly because it's stored in NotificationState.
        // We will need to join that later. For now, it passes.

        return true;
    }

    [RelayCommand]
    private async Task RefreshDataAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        LastSyncStatus = "Đang đồng bộ...";

        try
        {
            var sourcePath = _settingsService.Load().SourceFilePath;

            if (!string.IsNullOrEmpty(sourcePath) && File.Exists(sourcePath))
            {
                // File I/O and SQLite run on a worker thread; only UI updates marshal back.
                var diagnostics = await Task.Run(async () =>
                {
                    await using var stream = File.OpenRead(sourcePath);
                    // StableFileReader-style guard: a mid-write file yields an error
                    // diagnostics row instead of a crash; the last good snapshot stays current.
                    return _importUseCase.Execute(GetStableFileId(sourcePath), stream);
                });

                if (diagnostics.ErrorMessage != null)
                {
                    LastSyncStatus = $"Lỗi đọc file: {diagnostics.ErrorMessage}";
                }
                else
                {
                    LastSyncStatus = $"Đã nhập {diagnostics.ValidRowsImported} dòng lúc {DateTime.Now:HH:mm:ss}";
                }

                ReloadTasksFromDb();
            }
            else
            {
                LastSyncStatus = "Chưa chọn file nguồn (mở Cài đặt)";
            }

            UpdateSummaryCounts();
        }
        catch (Exception ex)
        {
            LastSyncStatus = $"Lỗi: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Stable per-path id so logical row keys survive renames of the snapshot id.</summary>
    private static string GetStableFileId(string path) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToLowerInvariant())))[..32].ToLowerInvariant();

    public void ReloadTasksFromDb()
    {
        var sourcePath = _settingsService.Load().SourceFilePath;
        if (string.IsNullOrEmpty(sourcePath)) return;

        var fileId = GetStableFileId(sourcePath);
        var rows = _repository.GetCurrentRows(fileId);

        Tasks.Clear();
        foreach (var row in rows)
        {
            Tasks.Add(row);
        }
    }

    private void UpdateSummaryCounts()
    {
        OverdueCount = Tasks.Count(t => t.CurrentStatus == TaskStatus.Overdue);
        DueTodayCount = Tasks.Count(t => t.CurrentStatus == TaskStatus.DueToday);
        DueSoonCount = Tasks.Count(t => t.CurrentStatus == TaskStatus.DueSoon);
        NeedsReviewCount = Tasks.Count(t => t.CurrentStatus == TaskStatus.NeedsReview);
    }
}
