using System;
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaskTracker.Application;
using TaskTracker.Infrastructure.Excel;
using TaskTracker.Infrastructure.Persistence;

namespace TaskTracker.Windows;

public partial class App : System.Windows.Application
{
    public static IHost? AppHost { get; private set; }

    public App()
    {
        AppHost = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // UI
                services.AddSingleton<MainWindow>();
                services.AddSingleton<ViewModels.MainViewModel>();

                // Domain & Application
                services.AddSingleton<Domain.IClock, Windows.Infrastructure.SystemClock>(
                    _ => new Windows.Infrastructure.SystemClock(DateTimeOffset.UtcNow, DateOnly.FromDateTime(DateTime.Now)));
                // Using FakeClock just to resolve the interface for MVP; Ideally we should make a RealClock. Let's make a RealClock later.

                services.AddSingleton<Domain.DeadlineParser>();
                services.AddSingleton<Domain.ExcelDateResolver>();
                services.AddSingleton<Domain.TaskStatusCalculator>();
                services.AddSingleton<RowIdentityService>();
                services.AddSingleton<AlertEvaluatorService>(sp =>
                    new AlertEvaluatorService(null!, sp.GetRequiredService<Domain.IClock>())); // null repo for now to compile

                services.AddSingleton<ExcelReader>();
                services.AddSingleton<ImportWorkbookUseCase>();

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

        // Check if starting in background
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

        if (!runInBackground)
        {
            mainWindow.Show();
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
