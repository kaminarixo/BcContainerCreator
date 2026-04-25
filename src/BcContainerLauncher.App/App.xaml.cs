using System.IO;
using System.Windows;
using System.Windows.Threading;
using BcContainerLauncher.App.Logging;
using BcContainerLauncher.App.Services;
using BcContainerLauncher.App.ViewModels;
using BcContainerLauncher.Core.Containers;
using BcContainerLauncher.Core.Docker;
using BcContainerLauncher.Core.PowerShell;
using BcContainerLauncher.Core.Setup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

namespace BcContainerLauncher.App;

public partial class App : Application
{
    private IHost? _host;

    public IServiceProvider Services =>
        _host?.Services ?? throw new InvalidOperationException("Host nicht initialisiert.");

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Sink wird sehr früh erzeugt, weil der Logger über ihn schreibt UND
        // das LogViewModel ihn später per DI braucht.
        var sink = new InMemoryLogSink();

        // Log-Pfad: %ProgramData%\BcContainerLauncher\Logs\
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "BcContainerLauncher", "Logs");
        Directory.CreateDirectory(logDir);
        var logFile = Path.Combine(logDir, "bccontainerlauncher-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.File(
                path: logFile,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .WriteTo.Sink(sink)
            .CreateLogger();

        Log.Information("BC Container Launcher startet (PID {Pid})", Environment.ProcessId);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                // Logging über Serilog wiederverwenden.
                services.AddSingleton<ILoggerFactory>(_ => new SerilogLoggerFactory(Log.Logger, dispose: false));
                services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));

                // Sink als Singleton, sowohl als Konkretklasse als auch als Schnittstelle.
                services.AddSingleton(sink);

                // Core
                services.AddSingleton<IPowerShellRunner, PowerShellRunner>();
                services.AddSingleton<IDockerService, DockerService>();
                services.AddSingleton<IPreflightCheck, PreflightCheck>();
                services.AddSingleton<ISetupService, SetupService>();
                services.AddSingleton<IContainerService, ContainerService>();

                // App-Services
                services.AddSingleton<IDialogService, DialogService>();

                // ViewModels
                services.AddSingleton<DiagnosticsViewModel>();
                services.AddSingleton<CreateContainerViewModel>();
                services.AddSingleton<LogViewModel>();
                services.AddSingleton<MainViewModel>();

                // MainWindow
                services.AddSingleton<MainWindow>();
            })
            .Build();

        await _host.StartAsync();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

        var window = _host.Services.GetRequiredService<MainWindow>();
        window.DataContext = _host.Services.GetRequiredService<MainViewModel>();
        window.Show();

        // Diagnose initial automatisch starten — non-blocking.
        var diag = _host.Services.GetRequiredService<DiagnosticsViewModel>();
        if (diag.RunAllCommand.CanExecute(null))
        {
            diag.RunAllCommand.Execute(null);
        }

        base.OnStartup(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unbehandelter UI-Fehler");
        MessageBox.Show(
            e.Exception.Message,
            "Unerwarteter Fehler",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            Log.Fatal(ex, "Unbehandelter Domain-Fehler (IsTerminating={IsTerminating})", e.IsTerminating);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            // PowerShell-Runspace sauber schließen.
            if (_host is not null)
            {
                var runner = _host.Services.GetService<IPowerShellRunner>();
                if (runner is not null)
                {
                    await runner.DisposeAsync();
                }
                await _host.StopAsync(TimeSpan.FromSeconds(2));
                _host.Dispose();
            }
        }
        finally
        {
            Log.Information("BC Container Launcher beendet");
            await Log.CloseAndFlushAsync();
        }
        base.OnExit(e);
    }
}
