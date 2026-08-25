using System;
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaskTracker.Application;
using TaskTracker.Infrastructure.Excel;
using TaskTracker.Infrastructure.Persistence;
using TaskTracker.Application.Lifecycle;
using TaskTracker.Windows.Lifecycle;

namespace TaskTracker.Windows;

public partial class App : System.Windows.Application
{
    public static IHost? AppHost { get; private set; }

    private ISingleInstanceLock? _singleInstance;
    private AppLifecycleController? _lifecycle;
    private TrayIconService? _tray;
    private ISleepResumeMonitor? _sleepMonitor;

    public App()
    {
        AppHost = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // UI
                services.AddSingleton<MainWindow>();
                services.AddSingleton<ViewModels.MainViewModel>();

                // Domain & Application
                services.AddSingleton<Domain.IClock, Windows.Infrastructure.SystemClock>();

                services.AddSingleton<Domain.DeadlineParser>();
                services.AddSingleton<Domain.ExcelDateResolver>();
                services.AddSingleton<Domain.TaskStatusCalculator>();
                services.AddSingleton<RowIdentityService>();
                services.AddSingleton<AlertEvaluatorService>(sp =>
                    new AlertEvaluatorService(null!, sp.GetRequiredService<Domain.IClock>())); // null repo for now to compile

                services.AddSingleton<ExcelReader>();
                services.AddSingleton<ImportWorkbookUseCase>();
                services.AddSingleton<SqliteDeadlineResolutionRepository>();
                services.AddSingleton<ResolveDeadlineUseCase>();
                services.AddSingleton<Views.DeadlineReviewViewModel>(sp =>
                {
                    var resolve = sp.GetRequiredService<ResolveDeadlineUseCase>();
                    // The active file id is owned by MainViewModel; resolved lazily at action time.
                    return new Views.DeadlineReviewViewModel(resolve, () => "default-file");
                });
                services.AddSingleton<Views.DeadlineReviewView>();

                // Lifecycle (TASK-17)
                services.AddSingleton<ISingleInstanceLock, SingleInstanceGuard>();
                services.AddSingleton<ISleepResumeMonitor, SleepResumeMonitor>();
                services.AddSingleton<AppLifecycleController>();
                services.AddSingleton<TrayIconService>();
                services.AddSingleton<MainWindowHandle>(sp =>
                    new MainWindowHandle(sp.GetRequiredService<MainWindow>()));

                // DB
                var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskTracker", "tasktracker.db");
                Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
                var connStr = $"Data Source={dbPath};Mode=ReadWriteCreate";

                services.AddSingleton<IDbConnectionFactory>(new SqliteConnectionFactory(connStr));
                services.AddSingleton<DatabaseMigrator>();
                services.AddSingleton<SqliteTaskRepository>();
            })
            .Build();
    }

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        await AppHost!.StartAsync();

        // Run migrations
        var migrator = AppHost.Services.GetRequiredService<DatabaseMigrator>();
        migrator.MigrateUp();

        // Single-instance enforcement: a secondary instance signals the primary
        // and shuts down immediately.
        _singleInstance = AppHost.Services.GetRequiredService<ISingleInstanceLock>();
        if (!_singleInstance.IsPrimaryInstance)
        {
            Shutdown();
            return;
        }
        _singleInstance.ActivateRequested += (_, _) =>
            Dispatcher.Invoke(() => _lifecycle?.Activate());

        // Check if starting in background (--background: start hidden in tray)
        bool runInBackground = false;
        foreach (var arg in e.Args)
        {
            if (arg.Equals("--background", StringComparison.OrdinalIgnoreCase))
            {
                runInBackground = true;
                break;
            }
        }

        var mainWindow = AppHost.Services.GetRequiredService<MainWindow>();
        var windowHandle = AppHost.Services.GetRequiredService<MainWindowHandle>();
        _tray = AppHost.Services.GetRequiredService<TrayIconService>();
        var mainVm = AppHost.Services.GetRequiredService<ViewModels.MainViewModel>();

        _lifecycle = new AppLifecycleController(
            windowHandle,
            _tray,
            tooltipProvider: () =>
            {
                var pending = mainVm.OverdueCount + mainVm.DueTodayCount + mainVm.DueSoonCount;
                return pending > 0
                    ? $"Task Tracker — {pending} nhiệm vụ cần chú ý"
                    : "Task Tracker";
            });

        // Close button hides to tray instead of quitting.
        mainWindow.Closing += (_, args) =>
        {
            if (_lifecycle.CloseToTrayEnabled)
            {
                args.Cancel = true;
                _lifecycle.OnCloseRequested();
            }
        };

        // Tray "Thoát" performs the real shutdown.
        _lifecycle.ExitRequested += (_, _) =>
        {
            _lifecycle.CloseToTrayEnabled = false; // next close is a real quit
            Shutdown();
        };

        // Re-evaluate alerts when Windows wakes from sleep.
        _sleepMonitor = AppHost.Services.GetRequiredService<ISleepResumeMonitor>();
        _sleepMonitor.ResumedFromSleep += (_, _) =>
            Dispatcher.Invoke(() =>
            {
                _ = mainVm.RefreshDataCommand.ExecuteAsync(null);
                _tray?.UpdateTooltip("Task Tracker — đã đồng bộ lại sau sleep");
            });

        _lifecycle.OnStartup(runInBackground);
    }

    private async void Application_Exit(object sender, ExitEventArgs e)
    {
        _sleepMonitor?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();

        if (AppHost != null)
        {
            await AppHost.StopAsync();
            AppHost.Dispose();
        }
    }
}
