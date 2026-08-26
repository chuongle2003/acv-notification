using System;
using System.Windows;
using System.Windows.Controls;
using System.Threading.Tasks;
using TaskTracker.Application;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MessageBox = System.Windows.MessageBox;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace TaskTracker.Windows.Views;

/// <summary>
/// Modal settings dialog: source file path, notification pause, auto-start.
/// Repeat interval is fixed at 12h in MVP and shown read-only.
/// </summary>
public class SettingsDialog : Window
{
    private readonly AppSettings _settings;
    private readonly Func<string, bool>? _validatePath;
    private readonly Func<Task<bool>>? _sendTestNotification;

    public AppSettings ResultSettings { get; private set; }

    private readonly TextBox _pathBox;
    private readonly CheckBox _pauseCheck;
    private readonly CheckBox _autoStartCheck;

    public SettingsDialog(
        AppSettings settings,
        Func<string, bool>? validatePath = null,
        Func<Task<bool>>? sendTestNotification = null)
    {
        _settings = settings;
        _validatePath = validatePath;
        _sendTestNotification = sendTestNotification;
        ResultSettings = new AppSettings();

        Title = "Cài đặt";
        Width = 520;
        Height = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var stack = new StackPanel { Margin = new Thickness(20) };

        // --- Source file ---
        stack.Children.Add(new TextBlock
        {
            Text = "File Excel nguồn:",
            FontSize = 12,
            Foreground = System.Windows.Media.Brushes.Gray
        });

        var pathRow = new DockPanel { Margin = new Thickness(0, 4, 0, 12) };
        var browseBtn = new Button { Content = "Chọn...", Padding = new Thickness(10, 4, 10, 4) };
        DockPanel.SetDock(browseBtn, Dock.Right);
        _pathBox = new TextBox
        {
            Text = settings.SourceFilePath,
            Padding = new Thickness(4),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        browseBtn.Click += (_, _) => BrowseForFile();
        pathRow.Children.Add(browseBtn);
        pathRow.Children.Add(_pathBox);
        stack.Children.Add(pathRow);

        // --- Notifications ---
        _pauseCheck = new CheckBox
        {
            Content = "Tạm dừng thông báo",
            IsChecked = settings.NotificationsPaused,
            Margin = new Thickness(0, 4, 0, 4)
        };
        stack.Children.Add(_pauseCheck);

        stack.Children.Add(new TextBlock
        {
            Text = "Nhắc lại mỗi 12 giờ (cố định trong phiên bản này)",
            FontSize = 12,
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 0, 0, 8)
        });

        // --- Auto start ---
        _autoStartCheck = new CheckBox
        {
            Content = "Khởi động cùng Windows (chạy nền)",
            IsChecked = settings.StartWithWindows,
            Margin = new Thickness(0, 4, 0, 16)
        };
        stack.Children.Add(_autoStartCheck);

        // --- Buttons ---
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        if (_sendTestNotification != null)
        {
            var test = new Button
            {
                Content = "Gửi thông báo thử",
                Padding = new Thickness(12, 5, 12, 5),
                Margin = new Thickness(0, 0, 16, 0)
            };
            test.Click += async (_, _) =>
            {
                test.IsEnabled = false;
                try
                {
                    var sent = await _sendTestNotification();
                    if (!sent)
                    {
                        MessageBox.Show(this,
                            "Không gửi được thông báo. Hãy kiểm tra Windows Notifications/Do Not Disturb.",
                            "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                finally
                {
                    test.IsEnabled = true;
                }
            };
            buttons.Children.Add(test);
        }

        var ok = new Button { Content = "Lưu", Padding = new Thickness(16, 5, 16, 5), IsDefault = true };
        var cancel = new Button
        {
            Content = "Hủy",
            Padding = new Thickness(16, 5, 16, 5),
            Margin = new Thickness(8, 0, 0, 0),
            IsCancel = true
        };

        ok.Click += (_, _) => SaveAndClose();
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        stack.Children.Add(buttons);

        Content = stack;
    }

    private void BrowseForFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Chọn file Excel nguồn",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            _pathBox.Text = dialog.FileName;
        }
    }

    private void SaveAndClose()
    {
        var path = _pathBox.Text.Trim();

        if (_validatePath != null && !string.IsNullOrEmpty(path) && !_validatePath(path))
        {
            MessageBox.Show(this, "File không tồn tại hoặc không đọc được.", "Lỗi",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ResultSettings = new AppSettings
        {
            SourceFilePath = path,
            NotificationsPaused = _pauseCheck.IsChecked == true,
            StartWithWindows = _autoStartCheck.IsChecked == true,
            RepeatIntervalMinutes = _settings.RepeatIntervalMinutes // unchanged, read-only
        };

        DialogResult = true;
    }
}
