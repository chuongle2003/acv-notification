using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskTracker.Application;
using TaskTracker.Domain;
using TaskTracker.Infrastructure.Persistence;

namespace TaskTracker.Windows.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ImportWorkbookUseCase _importUseCase;
    private readonly SqliteTaskRepository _repository;

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

    public MainViewModel(ImportWorkbookUseCase importUseCase, SqliteTaskRepository repository)
    {
        _importUseCase = importUseCase;
        _repository = repository;

        _tasksView = CollectionViewSource.GetDefaultView(Tasks);
        _tasksView.Filter = FilterTask;

        // Sorting
        _tasksView.SortDescriptions.Add(new SortDescription(nameof(TaskRow.CurrentStatus), ListSortDirection.Ascending));
        _tasksView.SortDescriptions.Add(new SortDescription(nameof(TaskRow.SheetWeekNumber), ListSortDirection.Descending));
        _tasksView.SortDescriptions.Add(new SortDescription(nameof(TaskRow.SourceRowNumber), ListSortDirection.Ascending));
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
            // Just simulating load for UI structure since we don't have file picking wired up yet
            await Task.Delay(500);

            // Update stats dummy
            OverdueCount = Tasks.Count(t => t.CurrentStatus == TaskStatus.Overdue);
            DueTodayCount = Tasks.Count(t => t.CurrentStatus == TaskStatus.DueToday);
            DueSoonCount = Tasks.Count(t => t.CurrentStatus == TaskStatus.DueSoon);
            NeedsReviewCount = Tasks.Count(t => t.CurrentStatus == TaskStatus.NeedsReview);

            LastSyncStatus = $"Cập nhật lúc {DateTime.Now:HH:mm:ss}";
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
}
