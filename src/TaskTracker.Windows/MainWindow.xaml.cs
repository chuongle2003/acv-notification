using System;
using System.IO;
using System.Windows;
using TaskTracker.Application;
using TaskTracker.Application.Lifecycle;
using TaskTracker.Windows.Lifecycle;
using TaskTracker.Windows.ViewModels;
using TaskTracker.Windows.Views;
using MessageBox = System.Windows.MessageBox;

namespace TaskTracker.Windows;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly SettingsService _settingsService;
    private readonly IAutoStartRegistrar _autoStartRegistrar;

    public MainWindow(MainViewModel viewModel, DeadlineReviewView reviewView,
        SettingsService settingsService, IAutoStartRegistrar autoStartRegistrar)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settingsService = settingsService;
        _autoStartRegistrar = autoStartRegistrar;
        DataContext = viewModel;

        // DeadlineReviewView requires constructor injection (no default ctor), so
        // it is hosted via ContentControl instead of being declared in XAML.
        ReviewHost.Content = reviewView;

        Loaded += async (_, _) => await viewModel.RefreshDataCommand.ExecuteAsync(null);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var current = _settingsService.Load();
        var dialog = new SettingsDialog(current,
            validatePath: path => File.Exists(path))
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true) return;

        var newSettings = dialog.ResultSettings;

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

        // A changed source path shows up in the header right away.
        if (!string.IsNullOrEmpty(newSettings.SourceFilePath))
        {
            _viewModel.SourceFileName = Path.GetFileName(newSettings.SourceFilePath);
        }
    }

    private void OnChangeSourceFileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Chọn file Excel nguồn",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true) return;

        var settings = _settingsService.Load();
        settings.SourceFilePath = dialog.FileName;
        _settingsService.Save(settings);
        _viewModel.SourceFileName = Path.GetFileName(dialog.FileName);

        _ = _viewModel.RefreshDataCommand.ExecuteAsync(null);
    }
}
