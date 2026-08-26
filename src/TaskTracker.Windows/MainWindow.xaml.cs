using System;
using System.IO;
using System.Linq;
using System.Windows;
using TaskTracker.Application;
using TaskTracker.Application.Lifecycle;
using TaskTracker.Windows.Lifecycle;
using TaskTracker.Windows.ViewModels;
using TaskTracker.Windows.Views;
using TaskTracker.Windows.Notifications;
using MessageBox = System.Windows.MessageBox;

namespace TaskTracker.Windows;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly SettingsService _settingsService;
    private readonly IAutoStartRegistrar _autoStartRegistrar;
    private readonly SourceMonitorService _sourceMonitor;
    private readonly DeadlineReviewViewModel _reviewViewModel;
    private readonly ResolveDeadlineUseCase _resolveDeadlineUseCase;
    private readonly WindowsAppNotificationSink _notificationSink;
    private readonly TrayIconService _tray;

    public MainWindow(MainViewModel viewModel, DeadlineReviewView reviewView,
        DeadlineReviewViewModel reviewViewModel,
        SettingsService settingsService,
        IAutoStartRegistrar autoStartRegistrar,
        SourceMonitorService sourceMonitor,
        ResolveDeadlineUseCase resolveDeadlineUseCase,
        WindowsAppNotificationSink notificationSink,
        TrayIconService tray)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settingsService = settingsService;
        _autoStartRegistrar = autoStartRegistrar;
        _sourceMonitor = sourceMonitor;
        _reviewViewModel = reviewViewModel;
        _resolveDeadlineUseCase = resolveDeadlineUseCase;
        _notificationSink = notificationSink;
        _tray = tray;
        DataContext = viewModel;

        // DeadlineReviewView requires constructor injection (no default ctor), so
        // it is hosted via ContentControl instead of being declared in XAML.
        ReviewHost.Content = reviewView;

        viewModel.DataReloaded += (_, _) => ReloadReviewItems();
        reviewViewModel.ReviewCompleted += (_, _) =>
        {
            viewModel.ReloadTasksFromDb();
            _ = viewModel.EvaluateNotificationsAsync();
        };

        Loaded += async (_, _) => await viewModel.RefreshDataCommand.ExecuteAsync(null);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var current = _settingsService.Load();
        var dialog = new SettingsDialog(
            current,
            validatePath: path => File.Exists(path),
            sendTestNotification: SendTestNotificationAsync)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true) return;

        var newSettings = dialog.ResultSettings;
        var sourceChanged = !string.Equals(
            current.SourceFilePath, newSettings.SourceFilePath, StringComparison.OrdinalIgnoreCase);
        if (sourceChanged && !string.IsNullOrWhiteSpace(newSettings.SourceFilePath))
        {
            var selected = _settingsService.SelectSourceFile(newSettings.SourceFilePath);
            newSettings.SourceFileId = selected.SourceFileId;
            newSettings.SourceFilePath = selected.SourceFilePath;
        }
        else if (!sourceChanged)
        {
            newSettings.SourceFileId = current.SourceFileId;
        }
        else
        {
            newSettings.SourceFileId = "";
        }

        // Auto-start follows the toggle immediately; failure is non-fatal.
        try
        {
            if (newSettings.StartWithWindows && !_autoStartRegistrar.IsEnabled())
            {
                _autoStartRegistrar.Enable(Environment.ProcessPath ?? "", new[] { "--background" });
            }
            else if (!newSettings.StartWithWindows && _autoStartRegistrar.IsEnabled())
            {
                _autoStartRegistrar.Disable();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Không cập nhật được auto-start: {ex.Message}", "Cảnh báo",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _settingsService.Save(newSettings);
        _tray.UpdatePaused(newSettings.NotificationsPaused);

        // A changed source path shows up in the header right away.
        if (!string.IsNullOrEmpty(newSettings.SourceFilePath))
        {
            _viewModel.SourceFileName = Path.GetFileName(newSettings.SourceFilePath);
            if (sourceChanged)
            {
                _sourceMonitor.Restart(newSettings.SourceFilePath);
                _ = _viewModel.RefreshDataCommand.ExecuteAsync(null);
            }
        }
        else if (sourceChanged)
        {
            _sourceMonitor.Stop();
            _viewModel.SourceFileName = "Chưa chọn file";
            _viewModel.ReloadTasksFromDb();
        }

        if (current.NotificationsPaused && !newSettings.NotificationsPaused)
            _ = _viewModel.EvaluateNotificationsAsync();
    }

    private async System.Threading.Tasks.Task<bool> SendTestNotificationAsync()
    {
        var task = new TaskTracker.Domain.TaskRow
        {
            SourceFileId = _viewModel.CurrentSourceFileId,
            LogicalRowKey = "test-notification",
            DeadlineVersion = "test",
            DocumentNumber = "Thông báo thử",
            TaskContent = "Task Tracker có thể gửi thông báo trên máy này.",
            CurrentStatus = TaskTracker.Domain.TaskStatus.DueSoon
        };
        return await _notificationSink.ShowIndividualAsync(new NotificationDecision
        {
            ShouldNotify = true,
            Task = task,
            Group = TaskTracker.Domain.AlertGroup.Upcoming
        });
    }

    private void OnChangeSourceFileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Chọn file Excel nguồn",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true) return;

        _settingsService.SelectSourceFile(dialog.FileName);
        _viewModel.SourceFileName = Path.GetFileName(dialog.FileName);

        _sourceMonitor.Restart(dialog.FileName);
        _ = _viewModel.RefreshDataCommand.ExecuteAsync(null);
    }

    private void OnResetCorrectionClick(object sender, RoutedEventArgs e)
    {
        var item = _viewModel.SelectedTask;
        if (item == null) return;

        var result = _resolveDeadlineUseCase.Reset(_viewModel.CurrentSourceFileId, item.LogicalRowKey);
        if (!result.Success)
        {
            MessageBox.Show(this, result.ErrorMessage, "Không thể xóa sửa chữa",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _viewModel.ReloadTasksFromDb();
        _ = _viewModel.EvaluateNotificationsAsync();
    }

    private void ReloadReviewItems()
    {
        var items = _viewModel.CurrentRows
            .Where(row => row.RequiresReview)
            .Select(row => new DeadlineReviewItemViewModel(
                row,
                row.DeadlineKind,
                row.ExcelCandidate,
                row.SwappedCandidate,
                ProblemLabel(row.DeadlineKind)));
        _reviewViewModel.LoadItems(items);
    }

    private static string ProblemLabel(TaskTracker.Domain.DeadlineParserKind kind) => kind switch
    {
        TaskTracker.Domain.DeadlineParserKind.ExcelDateAmbiguous => "Nghi đảo ngày/tháng",
        TaskTracker.Domain.DeadlineParserKind.MissingYear => "Thiếu năm",
        TaskTracker.Domain.DeadlineParserKind.MonthOnly => "Chỉ có tháng",
        TaskTracker.Domain.DeadlineParserKind.WeekOnly => "Chỉ có tuần",
        TaskTracker.Domain.DeadlineParserKind.RecurringUnconfigured => "Lặp chưa cấu hình",
        _ => "Không rõ định dạng"
    };
}
