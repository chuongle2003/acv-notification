using System;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaskTracker.Application;
using TaskTracker.Application.Lifecycle;
using TaskTracker.Domain;
using TaskTracker.Infrastructure.Excel;
using TaskTracker.Infrastructure.FileWatching;
using TaskTracker.Infrastructure.Persistence;
using TaskTracker.Windows.Lifecycle;
using TaskTracker.Windows.Notifications;
using TaskTracker.Windows.ViewModels;
using TaskTracker.Windows.Views;

namespace TaskTracker.Windows;

public partial class App : System.Windows.Application
{
    public static IHost? AppHost { get; private set; }

    private ISingleInstanceLock? _singleInstance;
    private AppLifecycleController? _lifecycle;
    private TrayIconService? _tray;
    private ISleepResumeMonitor? _sleepMonitor;
    private SourceMonitorService? _sourceMonitor;
    private AlertSchedulerService? _alertScheduler;
    private WindowsAppNotificationSink? _notificationSink;

    public App()
    {
        AppHost = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) => ConfigureServices(services))
            .Build();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskTracker",
            "tasktracker.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        services.AddSingleton<IDbConnectionFactory>(
            new SqliteConnectionFactory($"Data Source={dbPath};Mode=ReadWriteCreate"));
        services.AddSingleton<DatabaseMigrator>();

        services.AddSingleton<IClock, Infrastructure.SystemClock>();
        services.AddSingleton<DeadlineParser>();
        services.AddSingleton<ExcelDateResolver>();
        services.AddSingleton<TaskStatusCalculator>();
        services.AddSingleton<RowIdentityService>();

        services.AddSingleton<ExcelReader>();
        services.AddSingleton<IExcelWorkbookReader>(sp => sp.GetRequiredService<ExcelReader>());
        services.AddSingleton<SqliteTaskRepository>();
        services.AddSingleton<ITaskRowStore>(sp => sp.GetRequiredService<SqliteTaskRepository>());
        services.AddSingleton<SqliteDeadlineResolutionRepository>();
        services.AddSingleton<IResolutionStore>(sp => sp.GetRequiredService<SqliteDeadlineResolutionRepository>());
        services.AddSingleton<SqliteSettingsStore>();
        services.AddSingleton<ISettingsStore>(sp => sp.GetRequiredService<SqliteSettingsStore>());
        services.AddSingleton<SqliteSourceFileStateStore>();
        services.AddSingleton<ISourceFileStateStore>(sp => sp.GetRequiredService<SqliteSourceFileStateStore>());
        services.AddSingleton<SqliteNotificationStateRepository>();
        services.AddSingleton<INotificationStateRepository>(sp =>
            sp.GetRequiredService<SqliteNotificationStateRepository>());

        services.AddSingleton<StableFileReader>();
        services.AddSingleton<IStableFileReader>(sp => sp.GetRequiredService<StableFileReader>());
        services.AddSingleton<FileWatcherService>();

        services.AddSingleton<SettingsService>();
        services.AddSingleton<ImportWorkbookUseCase>();
        services.AddSingleton<IWorkbookImporter>(sp => sp.GetRequiredService<ImportWorkbookUseCase>());
        services.AddSingleton<RefreshSourceFileUseCase>();
        services.AddSingleton<ResolveDeadlineUseCase>();
        services.AddSingleton<AlertEvaluatorService>();
        services.AddSingleton<AcknowledgeAlertUseCase>();

        services.AddSingleton<WindowsAppNotificationSink>();
        services.AddSingleton<IAppNotificationSink>(sp => sp.GetRequiredService<WindowsAppNotificationSink>());
        services.AddSingleton<NotificationCoordinator>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<DeadlineReviewViewModel>(sp => new DeadlineReviewViewModel(
            sp.GetRequiredService<ResolveDeadlineUseCase>(),
            () => sp.GetRequiredService<SettingsService>().Load().SourceFileId));
        services.AddSingleton<DeadlineReviewView>();
        services.AddSingleton<MainWindow>();

        services.AddSingleton<ISingleInstanceLock, SingleInstanceGuard>();
        services.AddSingleton<ISleepResumeMonitor, SleepResumeMonitor>();
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<MainWindowHandle>(sp =>
            new MainWindowHandle(sp.GetRequiredService<MainWindow>()));
        services.AddSingleton<SourceMonitorService>();
        services.AddSingleton<AlertSchedulerService>();
        services.AddSingleton<IAutoStartRegistrar, RegistryAutoStartRegistrar>();
    }

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        await AppHost!.StartAsync();
        AppHost.Services.GetRequiredService<DatabaseMigrator>().MigrateUp();

        _singleInstance = AppHost.Services.GetRequiredService<ISingleInstanceLock>();
        if (!_singleInstance.IsPrimaryInstance)
        {
            Shutdown();
            return;
        }

        var mainWindow = AppHost.Services.GetRequiredService<MainWindow>();
        var windowHandle = AppHost.Services.GetRequiredService<MainWindowHandle>();
        var mainVm = AppHost.Services.GetRequiredService<MainViewModel>();
        var settingsService = AppHost.Services.GetRequiredService<SettingsService>();

        _tray = AppHost.Services.GetRequiredService<TrayIconService>();
        _lifecycle = new AppLifecycleController(windowHandle, _tray, () =>
        {
            var pending = mainVm.OverdueCount + mainVm.DueTodayCount + mainVm.DueSoonCount;
            return pending > 0
                ? $"Task Tracker — {pending} nhiệm vụ cần chú ý"
                : "Task Tracker";
        });

        _singleInstance.ActivateRequested += (_, _) =>
            Dispatcher.Invoke(() => _lifecycle.Activate());

        mainWindow.Closing += (_, args) =>
        {
            if (_lifecycle.CloseToTrayEnabled)
            {
                args.Cancel = true;
                _lifecycle.OnCloseRequested();
            }
        };

        _tray.OpenRequested += (_, _) => Dispatcher.Invoke(() => _lifecycle.Activate());
        _tray.RefreshRequested += (_, _) => Dispatcher.Invoke(() => _sourceMonitor?.TriggerManualRefresh());
        _tray.PauseToggleRequested += (_, _) => Dispatcher.Invoke(async () =>
        {
            var settings = settingsService.Load();
            settings.NotificationsPaused = !settings.NotificationsPaused;
            settingsService.Save(settings);
            _tray.UpdatePaused(settings.NotificationsPaused);
            if (!settings.NotificationsPaused) await mainVm.EvaluateNotificationsAsync();
        });
        _tray.ExitRequested += (_, _) => Dispatcher.Invoke(() =>
        {
            _lifecycle.CloseToTrayEnabled = false;
            Shutdown();
        });

        _sourceMonitor = AppHost.Services.GetRequiredService<SourceMonitorService>();
        _sourceMonitor.StartFromSettings();
        _alertScheduler = AppHost.Services.GetRequiredService<AlertSchedulerService>();
        _alertScheduler.Start();

        _sleepMonitor = AppHost.Services.GetRequiredService<ISleepResumeMonitor>();
        _sleepMonitor.ResumedFromSleep += (_, _) => Dispatcher.Invoke(() =>
        {
            _sourceMonitor.TriggerManualRefresh();
            _tray.UpdateTooltip("Task Tracker — đang đồng bộ lại sau sleep");
        });

        _notificationSink = AppHost.Services.GetRequiredService<WindowsAppNotificationSink>();
        _notificationSink.Activated += (_, activation) =>
            Dispatcher.Invoke(() => HandleNotificationActivation(activation, mainVm));
        try
        {
            _notificationSink.Initialize();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Notification registration failed: {ex.Message}");
        }

        var settings = settingsService.Load();
        _tray.UpdatePaused(settings.NotificationsPaused);
        SynchronizeAutoStart(settings);

        var runInBackground = e.Args.Any(arg =>
            arg.Equals("--background", StringComparison.OrdinalIgnoreCase));
        _lifecycle.OnStartup(runInBackground);
    }

    private void HandleNotificationActivation(NotificationActivation activation, MainViewModel mainVm)
    {
        if (activation.Action.Equals("ack", StringComparison.OrdinalIgnoreCase) &&
            activation.LogicalRowKey != null &&
            activation.DeadlineVersion != null &&
            activation.AlertGroup != null)
        {
            AppHost!.Services.GetRequiredService<INotificationStateRepository>().Acknowledge(
                activation.LogicalRowKey,
                activation.DeadlineVersion,
                activation.AlertGroup.Value,
                AppHost.Services.GetRequiredService<IClock>().UtcNow);
            mainVm.ReloadTasksFromDb();
            return;
        }

        _lifecycle?.Activate();
        if (!string.IsNullOrWhiteSpace(activation.LogicalRowKey))
            mainVm.SelectTask(activation.LogicalRowKey);
    }

    private void SynchronizeAutoStart(AppSettings settings)
    {
        var autoStart = AppHost!.Services.GetRequiredService<IAutoStartRegistrar>();
        try
        {
            if (settings.StartWithWindows && !autoStart.IsEnabled())
                autoStart.Enable(Environment.ProcessPath ?? "", new[] { "--background" });
            else if (!settings.StartWithWindows && autoStart.IsEnabled())
                autoStart.Disable();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Auto-start sync failed: {ex.Message}");
        }
    }

    private async void Application_Exit(object sender, ExitEventArgs e)
    {
        if (AppHost != null)
        {
            await AppHost.StopAsync();
            AppHost.Dispose();
        }
    }
}
