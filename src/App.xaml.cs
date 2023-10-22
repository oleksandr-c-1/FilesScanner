using FilesScanner.Interfaces;
using FilesScanner.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace FilesScanner;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application {
    readonly ILogger<App> _logger;
    readonly IHost _host = Host.CreateDefaultBuilder()
                               .ConfigureServices(ConfigureServices)
                               .Build();

    public App() {
        ApplicationServiceLocator.Services = _host.Services;
        var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "fileScanner.log");
        var logger = new LoggerConfiguration()
#if DEBUG
                    .MinimumLevel.Debug()
#else
                    .MinimumLevel.Information()
#endif
                 .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                 .CreateLogger();
        Log.Logger = logger;
        _logger = ApplicationServiceLocator.GetService<ILogger<App>>();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomainUnhandledException;
    }

    void CurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e) {
        _logger.LogError($"DomainUnhandledException => {e}");
    }

    void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) {
        _logger.LogError($"DispatcherUnhandledException => {e}");
    }

    static void ConfigureServices(HostBuilderContext context, IServiceCollection services) {
        services.AddLogging(builder => builder.ClearProviders().AddSerilog())
                .AddSingleton<IDiskService, DiskService>()
                .AddSingleton<MainWindowViewModel>();
    }

    protected override async void OnStartup(StartupEventArgs e) {
        await _host.StartAsync();
    }
}