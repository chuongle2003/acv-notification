using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskTracker.Application;

namespace TaskTracker.Windows.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ImportWorkbookUseCase _importUseCase;

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

    public ObservableCollection<TaskTracker.Domain.TaskRow> Tasks { get; } = new();

    public MainViewModel(ImportWorkbookUseCase importUseCase)
    {
        _importUseCase = importUseCase;
    }

    [RelayCommand]
    private async Task RefreshDataAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        LastSyncStatus = "Đang đồng bộ...";

        try
        {
            // Simulate async file pick or read for now
            await Task.Delay(1000);

            // TODO: Actually use _importUseCase with selected file path

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
