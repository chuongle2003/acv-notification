using System;
using System.Threading;
using System.Windows;
using TaskTracker.Windows.ViewModels;

namespace TaskTracker.Windows.Lifecycle;

public sealed class AlertSchedulerService : IDisposable
{
    private readonly MainViewModel _viewModel;
    private System.Threading.Timer? _timer;

    public AlertSchedulerService(MainViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    public void Start()
    {
        _timer ??= new System.Threading.Timer(_ =>
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                _ = _viewModel.EvaluateNotificationsAsync());
        }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
