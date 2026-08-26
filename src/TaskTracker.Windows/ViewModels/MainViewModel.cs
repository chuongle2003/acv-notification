using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskTracker.Application;
using TaskTracker.Domain;
using TaskTracker.Infrastructure.Persistence;
using TaskStatus = TaskTracker.Domain.TaskStatus;

namespace TaskTracker.Windows.ViewModels;

public partial class TaskItemViewModel : ObservableObject
{
    private readonly AcknowledgeAlertUseCase _acknowledgeUseCase;

    public TaskRow Row { get; }
    public event EventHandler? Acknowledged;

    [ObservableProperty]
    private bool _isAcknowledged;

    public string LogicalRowKey => Row.LogicalRowKey;
    public string SheetName => Row.SheetName;
    public int SourceRowNumber => Row.SourceRowNumber;
    public string? Stt => Row.Stt;
    public string? DocumentNumber => Row.DocumentNumber;
    public string? TaskContent => Row.TaskContent;
    public string? ExecutingUnit => Row.ExecutingUnit;
    public string? PrimaryHandler => Row.PrimaryHandler;
    public string? DeadlineRaw => Row.DeadlineRaw;
    public string? DeadlineCellAddress => Row.DeadlineCellAddress;
    public string? Result => Row.Result;
    public TaskStatus CurrentStatus => Row.CurrentStatus;
    public bool CanAcknowledge => CurrentStatus is TaskStatus.DueSoon or TaskStatus.DueToday or TaskStatus.Overdue;
    public DateOnly? ResolvedStartDate => Row.ResolvedStartDate;
    public int? DaysRemaining => Row.DaysRemaining;

    public TaskItemViewModel(TaskRow row, AcknowledgeAlertUseCase acknowledgeUseCase)
    {
        Row = row;
        _acknowledgeUseCase = acknowledgeUseCase;
        _isAcknowledged = acknowledgeUseCase.IsAcknowledged(row);
    }

    [RelayCommand(CanExecute = nameof(CanAcknowledge))]
    private void Acknowledge()
    {
        _acknowledgeUseCase.Execute(Row);
        IsAcknowledged = true;
        Acknowledged?.Invoke(this, EventArgs.Empty);
    }
}

public partial class MainViewModel : ObservableObject
{
    private readonly RefreshSourceFileUseCase _refreshUseCase;
    private readonly SqliteTaskRepository _repository;
    private readonly SettingsService _settingsService;
    private readonly AcknowledgeAlertUseCase _acknowledgeUseCase;
    private readonly NotificationCoordinator _notificationCoordinator;

    [ObservableProperty] private string _sourceFileName = "Chưa chọn file";
    [ObservableProperty] private string _lastSyncStatus = "Chưa đồng bộ";
    [ObservableProperty] private int _overdueCount;
    [ObservableProperty] private int _dueTodayCount;
    [ObservableProperty] private int _dueSoonCount;
    [ObservableProperty] private int _normalCount;
    [ObservableProperty] private int _completedCount;
    [ObservableProperty] private int _needsReviewCount;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _showUnreadOnly;
    [ObservableProperty] private string? _selectedSheet;
    [ObservableProperty] private string? _selectedHandler;
    [ObservableProperty] private TaskStatus? _selectedStatus;
    [ObservableProperty] private TaskItemViewModel? _selectedTask;

    public ObservableCollection<TaskItemViewModel> Tasks { get; } = new();
    public ObservableCollection<string> SheetOptions { get; } = new();
    public ObservableCollection<string> HandlerOptions { get; } = new();
    public IReadOnlyList<TaskStatus?> StatusOptions { get; } = new TaskStatus?[]
    {
        null, TaskStatus.Overdue, TaskStatus.DueToday, TaskStatus.DueSoon,
        TaskStatus.NeedsReview, TaskStatus.Normal, TaskStatus.Completed
    };

    private readonly ICollectionView _tasksView;
    public ICollectionView TasksView => _tasksView;
    public string CurrentSourceFileId => _settingsService.Load().SourceFileId;
    public IReadOnlyList<TaskRow> CurrentRows => Tasks.Select(item => item.Row).ToList();

    public event EventHandler? DataReloaded;

    public MainViewModel(
        RefreshSourceFileUseCase refreshUseCase,
        SqliteTaskRepository repository,
        SettingsService settingsService,
        AcknowledgeAlertUseCase acknowledgeUseCase,
        NotificationCoordinator notificationCoordinator)
    {
        _refreshUseCase = refreshUseCase;
        _repository = repository;
        _settingsService = settingsService;
        _acknowledgeUseCase = acknowledgeUseCase;
        _notificationCoordinator = notificationCoordinator;

        _tasksView = CollectionViewSource.GetDefaultView(Tasks);
        _tasksView.Filter = FilterTask;
        if (_tasksView is ListCollectionView listView)
            listView.CustomSort = new TaskSeverityComparer();

        var settings = _settingsService.Load();
        if (!string.IsNullOrWhiteSpace(settings.SourceFilePath))
            SourceFileName = Path.GetFileName(settings.SourceFilePath);
    }

    partial void OnSearchTextChanged(string value) => _tasksView.Refresh();
    partial void OnShowUnreadOnlyChanged(bool value) => _tasksView.Refresh();
    partial void OnSelectedSheetChanged(string? value) => _tasksView.Refresh();
    partial void OnSelectedHandlerChanged(string? value) => _tasksView.Refresh();
    partial void OnSelectedStatusChanged(TaskStatus? value) => _tasksView.Refresh();

    private bool FilterTask(object obj)
    {
        if (obj is not TaskItemViewModel item) return false;
        if (ShowUnreadOnly && item.IsAcknowledged) return false;
        if (SelectedSheet != null && item.SheetName != SelectedSheet) return false;
        if (SelectedHandler != null && item.PrimaryHandler != SelectedHandler) return false;
        if (SelectedStatus != null && item.CurrentStatus != SelectedStatus) return false;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.Trim();
            if (!(item.DocumentNumber?.Contains(search, StringComparison.OrdinalIgnoreCase) == true ||
                  item.TaskContent?.Contains(search, StringComparison.OrdinalIgnoreCase) == true))
                return false;
        }

        return true;
    }

    [RelayCommand]
    private async Task RefreshDataAsync(CancellationToken cancellationToken)
    {
        if (IsLoading) return;
        IsLoading = true;
        LastSyncStatus = "Đang đồng bộ...";

        try
        {
            var settings = _settingsService.Load();
            if (string.IsNullOrWhiteSpace(settings.SourceFileId) ||
                string.IsNullOrWhiteSpace(settings.SourceFilePath))
            {
                LastSyncStatus = "Chưa chọn file nguồn (mở Cài đặt)";
                return;
            }

            var result = await _refreshUseCase.ExecuteAsync(
                settings.SourceFileId, settings.SourceFilePath, cancellationToken);

            LastSyncStatus = result.Status switch
            {
                RefreshStatus.Imported =>
                    $"Đã nhập {result.Diagnostics?.ValidRowsImported ?? 0} dòng lúc {DateTime.Now:HH:mm:ss}",
                RefreshStatus.Unchanged => "File không thay đổi",
                RefreshStatus.Missing => "Không tìm thấy file nguồn",
                RefreshStatus.Invalid => "File không phải XLSX hợp lệ",
                RefreshStatus.TimedOut => "File đang được ghi hoặc bị khóa quá lâu",
                _ => $"Lỗi: {result.ErrorMessage}"
            };

            ReloadTasksFromDb();
            await EvaluateNotificationsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LastSyncStatus = "Đã hủy đồng bộ";
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

    public void ReloadTasksFromDb()
    {
        var settings = _settingsService.Load();
        if (string.IsNullOrWhiteSpace(settings.SourceFileId)) return;

        var rows = string.IsNullOrWhiteSpace(settings.SourceFileId)
            ? Array.Empty<TaskRow>()
            : _repository.GetCurrentRows(settings.SourceFileId);
        Tasks.Clear();
        foreach (var row in rows)
        {
            var item = new TaskItemViewModel(row, _acknowledgeUseCase);
            item.Acknowledged += (_, _) => _tasksView.Refresh();
            Tasks.Add(item);
        }

        RebuildFilterOptions();
        UpdateSummaryCounts();
        DataReloaded?.Invoke(this, EventArgs.Empty);
    }

    public async Task<int> EvaluateNotificationsAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settingsService.Load();
        return await _notificationCoordinator.EvaluateAndNotifyAsync(
            CurrentRows, settings.NotificationsPaused, cancellationToken);
    }

    public void SelectTask(string logicalRowKey)
    {
        var match = Tasks.FirstOrDefault(item => item.LogicalRowKey == logicalRowKey);
        if (match != null) SelectedTask = match;
    }

    private void RebuildFilterOptions()
    {
        ReplaceOptions(SheetOptions, Tasks.Select(item => item.SheetName));
        ReplaceOptions(HandlerOptions, Tasks.Select(item => item.PrimaryHandler));
    }

    private static void ReplaceOptions(ObservableCollection<string> target, IEnumerable<string?> values)
    {
        target.Clear();
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().Order())
            target.Add(value!);
    }

    private void UpdateSummaryCounts()
    {
        OverdueCount = Tasks.Count(item => item.CurrentStatus == TaskStatus.Overdue);
        DueTodayCount = Tasks.Count(item => item.CurrentStatus == TaskStatus.DueToday);
        DueSoonCount = Tasks.Count(item => item.CurrentStatus == TaskStatus.DueSoon);
        NormalCount = Tasks.Count(item => item.CurrentStatus == TaskStatus.Normal);
        CompletedCount = Tasks.Count(item => item.CurrentStatus == TaskStatus.Completed);
        NeedsReviewCount = Tasks.Count(item => item.CurrentStatus == TaskStatus.NeedsReview);
    }

    private sealed class TaskSeverityComparer : IComparer
    {
        private static readonly IReadOnlyDictionary<TaskStatus, int> Rank = new Dictionary<TaskStatus, int>
        {
            [TaskStatus.Overdue] = 0,
            [TaskStatus.DueToday] = 1,
            [TaskStatus.DueSoon] = 2,
            [TaskStatus.NeedsReview] = 3,
            [TaskStatus.Normal] = 4,
            [TaskStatus.Completed] = 5,
            [TaskStatus.Unknown] = 6
        };

        public int Compare(object? x, object? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is not TaskItemViewModel left) return -1;
            if (y is not TaskItemViewModel right) return 1;

            var severity = Rank[left.CurrentStatus].CompareTo(Rank[right.CurrentStatus]);
            if (severity != 0) return severity;
            var week = Nullable.Compare(right.Row.SheetWeekNumber, left.Row.SheetWeekNumber);
            return week != 0 ? week : left.SourceRowNumber.CompareTo(right.SourceRowNumber);
        }
    }
}
