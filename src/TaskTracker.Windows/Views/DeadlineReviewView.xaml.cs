using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskTracker.Application;
using TaskTracker.Domain;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MessageBox = System.Windows.MessageBox;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;
using UserControl = System.Windows.Controls.UserControl;

namespace TaskTracker.Windows.Views;

/// <summary>
/// One row in the review grid — wraps a TaskRow flagged for deadline review.
/// </summary>
public partial class DeadlineReviewItemViewModel : ObservableObject
{
    public TaskRow Row { get; }
    public DeadlineParserKind Kind { get; }
    public DateOnly? ExcelCandidate { get; }
    public DateOnly? SwappedCandidate { get; }

    public string SheetName => Row.SheetName;
    public int SourceRowNumber => Row.SourceRowNumber;
    public string? DocumentNumber => Row.DocumentNumber;
    public string? TaskContent => Row.TaskContent;
    public string ProblemLabel { get; }
    public string ExcelCandidateLabel => ExcelCandidate?.ToString("dd/MM/yyyy") ?? "—";
    public string SwappedCandidateLabel =>
        SwappedCandidate.HasValue ? SwappedCandidate.Value.ToString("dd/MM/yyyy") : "—";

    public DeadlineReviewItemViewModel(TaskRow row, DeadlineParserKind kind,
        DateOnly? excelCandidate, DateOnly? swappedCandidate, string problemLabel)
    {
        Row = row;
        Kind = kind;
        ExcelCandidate = excelCandidate;
        SwappedCandidate = swappedCandidate;
        ProblemLabel = problemLabel;
    }
}

public class KindFilterOption
{
    public string Label { get; init; } = "";
    public DeadlineParserKind? Kind { get; init; }
}

public partial class DeadlineReviewViewModel : ObservableObject
{
    private readonly ResolveDeadlineUseCase _resolveUseCase;
    private readonly Func<string> _sourceFileIdProvider;

    public ObservableCollection<DeadlineReviewItemViewModel> ReviewItems { get; } = new();

    [ObservableProperty]
    private int _totalPending;

    [ObservableProperty]
    private string _statusMessage = "Sẵn sàng";

    public IRelayCommand<DeadlineReviewItemViewModel> KeepCommand { get; }
    public IRelayCommand<DeadlineReviewItemViewModel> SwapCommand { get; }
    public IRelayCommand<DeadlineReviewItemViewModel> ManualCommand { get; }
    public IRelayCommand<DeadlineReviewItemViewModel> UnresolvedCommand { get; }

    public event EventHandler? ReviewCompleted;

    public DeadlineReviewViewModel(ResolveDeadlineUseCase resolveUseCase,
        Func<string> sourceFileIdProvider)
    {
        _resolveUseCase = resolveUseCase;
        _sourceFileIdProvider = sourceFileIdProvider;

        KeepCommand = new RelayCommand<DeadlineReviewItemViewModel>(item =>
            ExecuteAction(item, DeadlineReviewAction.KeepExcelDate));
        SwapCommand = new RelayCommand<DeadlineReviewItemViewModel>(item =>
            ExecuteAction(item, DeadlineReviewAction.UseSwappedDate));
        ManualCommand = new RelayCommand<DeadlineReviewItemViewModel>(ExecuteManual);
        UnresolvedCommand = new RelayCommand<DeadlineReviewItemViewModel>(item =>
            ExecuteAction(item, DeadlineReviewAction.MarkUnresolved));
    }

    public void LoadItems(IEnumerable<DeadlineReviewItemViewModel> items)
    {
        ReviewItems.Clear();
        foreach (var item in items) ReviewItems.Add(item);
        TotalPending = ReviewItems.Count;
    }

    private void ExecuteManual(DeadlineReviewItemViewModel? item)
    {
        if (item == null) return;

        var dialog = new ManualDateDialog(item.ExcelCandidate)
        {
            Owner = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        };
        var confirmed = dialog.ShowDialog();
        if (confirmed != true || dialog.SelectedDate == null) return;

        var result = ExecuteCore(new ResolveDeadlineRequest
        {
            SourceFileId = _sourceFileIdProvider(),
            LogicalRowKey = item.Row.LogicalRowKey,
            Action = DeadlineReviewAction.ManualDate,
            ManualDate = dialog.SelectedDate
        });

        if (result.Success) RemoveItem(item);
    }

    private void ExecuteAction(DeadlineReviewItemViewModel? item, DeadlineReviewAction action)
    {
        if (item == null) return;

        var result = ExecuteCore(new ResolveDeadlineRequest
        {
            SourceFileId = _sourceFileIdProvider(),
            LogicalRowKey = item.Row.LogicalRowKey,
            Action = action
        });

        if (result.Success) RemoveItem(item);
    }

    private ResolveDeadlineResult ExecuteCore(ResolveDeadlineRequest request)
    {
        var result = _resolveUseCase.Execute(request);

        StatusMessage = result.Success
            ? $"✓ Đã áp dụng cho dòng {request.LogicalRowKey[..Math.Min(8, request.LogicalRowKey.Length)]}…"
            : $"✗ Lỗi: {result.ErrorMessage}";

        if (result.Success) ReviewCompleted?.Invoke(this, EventArgs.Empty);
        return result;
    }

    private void RemoveItem(DeadlineReviewItemViewModel item)
    {
        ReviewItems.Remove(item);
        TotalPending = ReviewItems.Count;
    }
}

/// <summary>
/// Minimal modal dialog for manual date entry (dd/MM/yyyy).
/// </summary>
public class ManualDateDialog : Window
{
    public DateOnly? SelectedDate { get; private set; }

    public ManualDateDialog(DateOnly? suggestion)
    {
        Title = "Nhập ngày thủ công";
        Width = 320;
        Height = 170;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock
        {
            Text = "Ngày hạn chót (dd/MM/yyyy):",
            Margin = new Thickness(0, 0, 0, 8)
        });

        var input = new TextBox { Text = suggestion?.ToString("dd/MM/yyyy") ?? "", Padding = new Thickness(4) };
        stack.Children.Add(input);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };

        var ok = new Button { Content = "Xác nhận", Padding = new Thickness(12, 4, 12, 4), IsDefault = true };
        var cancel = new Button
        {
            Content = "Hủy",
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(8, 0, 0, 0),
            IsCancel = true
        };

        ok.Click += (_, _) =>
        {
            if (DateOnly.TryParseExact(input.Text.Trim(), "dd/MM/yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                SelectedDate = parsed;
                DialogResult = true;
            }
            else
            {
                MessageBox.Show(this, "Ngày không hợp lệ. Dùng định dạng dd/MM/yyyy.",
                    "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };

        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        stack.Children.Add(buttons);

        Content = stack;

        input.Focus();
        input.SelectAll();
    }
}

public partial class DeadlineReviewView : UserControl
{
    private readonly DeadlineReviewViewModel _viewModel;
    private ICollectionView? _itemsView;

    public DeadlineReviewView(DeadlineReviewViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        _itemsView = CollectionViewSource.GetDefaultView(viewModel.ReviewItems);
        ReviewGrid.ItemsSource = _itemsView;

        KindFilterCombo.ItemsSource = new[]
        {
            new KindFilterOption { Label = "Tất cả", Kind = null },
            new KindFilterOption { Label = "Nghi đảo ngày (Ambiguous)", Kind = DeadlineParserKind.ExcelDateAmbiguous },
            new KindFilterOption { Label = "Không rõ định dạng", Kind = DeadlineParserKind.Unrecognized },
            new KindFilterOption { Label = "Thiếu năm", Kind = DeadlineParserKind.MissingYear },
            new KindFilterOption { Label = "Chỉ có tháng", Kind = DeadlineParserKind.MonthOnly },
            new KindFilterOption { Label = "Định kỳ hàng tuần", Kind = DeadlineParserKind.WeekOnly }
        };
        KindFilterCombo.DisplayMemberPath = nameof(KindFilterOption.Label);
        KindFilterCombo.SelectedIndex = 0;
    }

    private void KindFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_itemsView == null) return;
        var selected = KindFilterCombo.SelectedItem as KindFilterOption;
        _itemsView.Filter = o => o is DeadlineReviewItemViewModel item &&
            (selected?.Kind == null || item.Kind == selected.Kind);
    }
}
