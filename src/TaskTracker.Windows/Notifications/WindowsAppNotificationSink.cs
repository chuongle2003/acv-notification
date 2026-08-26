using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using TaskTracker.Application;
using TaskTracker.Domain;

namespace TaskTracker.Windows.Notifications;

public sealed class WindowsAppNotificationSink : IAppNotificationSink, IDisposable
{
    private bool _registered;

    public event EventHandler<NotificationActivation>? Activated;

    public void Initialize()
    {
        if (_registered) return;
        var manager = AppNotificationManager.Default;
        manager.NotificationInvoked += OnNotificationInvoked;
        manager.Register();
        _registered = true;
    }

    public Task<bool> ShowIndividualAsync(
        NotificationDecision decision,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var task = decision.Task;
        if (task?.DeadlineVersion == null) return Task.FromResult(false);

        try
        {
            var builder = AddTaskArguments(
                    new AppNotificationBuilder(), "open", task, decision.Group)
                .AddText(decision.Group == AlertGroup.Overdue ? "Nhiệm vụ đã quá hạn" : "Nhiệm vụ sắp đến hạn")
                .AddText(task.DocumentNumber ?? task.TaskContent ?? "Nhiệm vụ cần chú ý")
                .AddButton(AddTaskArguments(
                    new AppNotificationButton("Mở"), "open", task, decision.Group))
                .AddButton(AddTaskArguments(
                    new AppNotificationButton("Đã xem"), "ack", task, decision.Group));

            AppNotificationManager.Default.Show(builder.BuildNotification());
            return Task.FromResult(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            System.Diagnostics.Debug.WriteLine($"App notification failed: {ex.Message}");
            return Task.FromResult(false);
        }
    }

    public Task<bool> ShowSummaryAsync(
        IReadOnlyList<NotificationDecision> decisions,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (decisions.Count == 0) return Task.FromResult(false);

        try
        {
            var overdue = decisions.Count(d => d.Group == AlertGroup.Overdue);
            var upcoming = decisions.Count - overdue;
            var sourceFileId = decisions.Select(d => d.Task?.SourceFileId)
                .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id)) ?? "";

            var builder = new AppNotificationBuilder()
                .AddArgument("action", "open-list")
                .AddArgument("sourceFileId", sourceFileId)
                .AddText($"{decisions.Count} nhiệm vụ cần chú ý")
                .AddText($"Quá hạn: {overdue} · Sắp đến hạn: {upcoming}")
                .AddButton(new AppNotificationButton("Mở danh sách")
                    .AddArgument("action", "open-list")
                    .AddArgument("sourceFileId", sourceFileId));

            AppNotificationManager.Default.Show(builder.BuildNotification());
            return Task.FromResult(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            System.Diagnostics.Debug.WriteLine($"Summary notification failed: {ex.Message}");
            return Task.FromResult(false);
        }
    }

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        Activated?.Invoke(this, NotificationActivationParser.Parse(args.Argument));
    }

    private static AppNotificationBuilder AddTaskArguments(
        AppNotificationBuilder builder,
        string action,
        TaskRow task,
        AlertGroup group) => builder
            .AddArgument("action", action)
            .AddArgument("sourceFileId", task.SourceFileId)
            .AddArgument("logicalRowKey", task.LogicalRowKey)
            .AddArgument("deadlineVersion", task.DeadlineVersion ?? "")
            .AddArgument("alertGroup", group.ToString());

    private static AppNotificationButton AddTaskArguments(
        AppNotificationButton button,
        string action,
        TaskRow task,
        AlertGroup group) => button
            .AddArgument("action", action)
            .AddArgument("sourceFileId", task.SourceFileId)
            .AddArgument("logicalRowKey", task.LogicalRowKey)
            .AddArgument("deadlineVersion", task.DeadlineVersion ?? "")
            .AddArgument("alertGroup", group.ToString());

    public void Dispose()
    {
        if (!_registered) return;
        var manager = AppNotificationManager.Default;
        manager.NotificationInvoked -= OnNotificationInvoked;
        manager.Unregister();
        _registered = false;
    }
}
