using CommunityToolkit.Mvvm.ComponentModel;
using TaskStatus = TaskTracker.Domain.TaskStatus;

namespace TaskTracker.Presentation;

public sealed record TextFilterOption(string Label, string Value)
{
    public override string ToString() => Label;
}

public sealed record StatusFilterOption(string Label, TaskStatus Value)
{
    public override string ToString() => Label;
}

public static class TaskStatusDisplay
{
    public static string GetLabel(TaskStatus status) => status switch
    {
        TaskStatus.Overdue => "Quá hạn",
        TaskStatus.DueToday => "Đến hạn hôm nay",
        TaskStatus.DueSoon => "Sắp đến hạn",
        TaskStatus.NeedsReview => "Cần rà soát",
        TaskStatus.Normal => "Bình thường",
        TaskStatus.Completed => "Đã hoàn thành",
        _ => "Chưa xác định"
    };
}

public static class ResolutionSourceDisplay
{
    public static string GetLabel(TaskTracker.Domain.ResolutionSource source) => source switch
    {
        TaskTracker.Domain.ResolutionSource.KeepExcelDate => "Giữ ngày Excel",
        TaskTracker.Domain.ResolutionSource.UseSwappedDate => "Đảo ngày/tháng",
        TaskTracker.Domain.ResolutionSource.ManualDate => "Nhập thủ công",
        TaskTracker.Domain.ResolutionSource.UnresolvedByUser => "Chưa xác định",
        _ => "Tự nhận diện"
    };
}

public static class DeadlineReviewActionAvailability
{
    public static bool CanKeepExcelDate(DateOnly? excelCandidate) => excelCandidate.HasValue;
    public static bool CanUseSwappedDate(DateOnly? swappedCandidate) => swappedCandidate.HasValue;
}

public partial class TaskFilterState : ObservableObject
{
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _showUnreadOnly;
    [ObservableProperty] private string _selectedSheet = "";
    [ObservableProperty] private string _selectedHandler = "";
    [ObservableProperty] private TaskStatus _selectedStatus = TaskStatus.Unknown;

    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(SearchText) ||
        ShowUnreadOnly ||
        !string.IsNullOrWhiteSpace(SelectedSheet) ||
        !string.IsNullOrWhiteSpace(SelectedHandler) ||
        SelectedStatus != TaskStatus.Unknown;

    public void Clear()
    {
        SearchText = "";
        ShowUnreadOnly = false;
        SelectedSheet = "";
        SelectedHandler = "";
        SelectedStatus = TaskStatus.Unknown;
    }

    partial void OnSearchTextChanged(string value) => NotifyFilterStateChanged();
    partial void OnShowUnreadOnlyChanged(bool value) => NotifyFilterStateChanged();
    partial void OnSelectedSheetChanged(string value) => NotifyFilterStateChanged();
    partial void OnSelectedHandlerChanged(string value) => NotifyFilterStateChanged();
    partial void OnSelectedStatusChanged(TaskStatus value) => NotifyFilterStateChanged();

    private void NotifyFilterStateChanged() => OnPropertyChanged(nameof(HasActiveFilters));
}
