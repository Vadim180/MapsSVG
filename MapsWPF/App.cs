using System;
using System.Windows;
using GMap.NET;
using MapsWPF.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace MapsWPF
{
    public partial class App : Application
    {
        [STAThread()]
        static void Main()
        {
            try
            {
                // Create the application.
                var app = new Application();

                // Create the DI container and register services
                var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
                // Register core services as singletons
                services.AddSingleton<Services.TemplateService>();
                services.AddSingleton<Services.MapService>(sp => new Services.MapService(new CoordinateConverter()));
                services.AddSingleton<Services.NotificationService>();
                services.AddSingleton<Services.ClipboardService>();
                // Settings manager as a singleton service
                services.AddSingleton<Services.SettingsService>();
                services.AddSingleton<Services.StatisticsService>();

                var provider = services.BuildServiceProvider();

                // Resolve services
                var settingsManager = provider.GetRequiredService<Services.SettingsService>();
                var statisticsService = provider.GetRequiredService<Services.StatisticsService>();

                // Create the main window with injected SettingsManager
                var win = new MainWindow(settingsManager, statisticsService);

                // Initialize ReportService using singletons from DI and the created MainWindow as selection/output providers
                var templateService = provider.GetRequiredService<Services.TemplateService>();
                var mapService = provider.GetRequiredService<Services.MapService>();
                var notificationService = provider.GetRequiredService<Services.NotificationService>();
                var clipboardService = provider.GetRequiredService<Services.ClipboardService>();

                // Ensure MainWindow uses the same TemplateService singleton from DI
                win.SetTemplateService(templateService);
                // Inject clipboard and notification services from DI into the MainWindow
                win.SetNotificationService(notificationService);
                win.SetClipboardService(clipboardService);

                var reportService = new Services.ReportService(templateService, mapService, notificationService, clipboardService, win, win);
                win.InitializeReportService(reportService);

                // Launch the application and show the main window.
                app.Run(win);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Startup exception: {ex}");
                Environment.Exit(1);
            }
        }
    }

    public class Dummy
    {
    }

    public struct PointAndInfo
    {
        public PointLatLng Point;
        public string Info;

        public PointAndInfo(PointLatLng point, string info)
        {
            Point = point;
            Info = info;
        }
    }
}
