using System.IO;
using System.Windows;
using System.Windows.Threading;
using BcContainerCreator.App.Logging;
using BcContainerCreator.App.Services;
using BcContainerCreator.App.ViewModels;
using BcContainerCreator.Core.Containers;
using BcContainerCreator.Core.Docker;
using BcContainerCreator.Core.PowerShell;
using BcContainerCreator.Core.Setup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace BcContainerCreator.App;

public partial class App : Application
{
    private IHost? _host;

    public IServiceProvider Services =>
        _host?.Services ?? throw new InvalidOperationException("Host nicht initialisiert.");

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Globale Exception-Handler ZUERST, damit jede Exception ab hier sichtbar ist.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

        try
        {
            await StartupCoreAsync();
        }
        catch (Exception ex)
        {
            // Letzter Fallback: Fehler vor dem Window-Show ist sonst unsichtbar.
            try { Log.Fatal(ex, "Startup fehlgeschlagen"); } catch { }
            MessageBox.Show(
                $"App konnte nicht starten:\n\n{ex}",
                "Startup-Fehler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }

        base.OnStartup(e);
    }

    private async Task StartupCoreAsync()
    {
        // Sink wird sehr früh erzeugt, weil der Logger über ihn schreibt UND
        // das LogViewModel ihn später per DI braucht.
        var sink = new InMemoryLogSink();

        // Log-Pfad: %ProgramData%\BcContainerCreator\Logs\
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "BcContainerCreator", "Logs");
        Directory.CreateDirectory(logDir);
        var logFile = Path.Combine(logDir, "BcContainerCreator-.log");

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

        Log.Information("BC Container Creator startet (PID {Pid}, OS {OS})",
            Environment.ProcessId, Environment.OSVersion);

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices((_, services) =>
            {
                // Sink als Singleton, sodass ihn das LogViewModel per DI bekommt.
                services.AddSingleton(sink);

                // Core
                services.AddSingleton<IPowerShellRunner, PowerShellRunner>();
                services.AddSingleton<IDockerService, DockerService>();
                services.AddSingleton<IPreflightCheck, PreflightCheck>();
                services.AddSingleton<IElevationService, ElevationService>();
                services.AddSingleton<ISetupService, SetupService>();
                services.AddSingleton<IContainerMetadataStore, ContainerMetadataStore>();
                services.AddSingleton<IContainerService, ContainerService>();

                // App-Services
                services.AddSingleton<IDialogService, DialogService>();

                // ViewModels
                services.AddSingleton<DiagnosticsViewModel>();
                services.AddSingleton<CreateContainerViewModel>();
                services.AddSingleton<ManageContainersViewModel>();
                services.AddSingleton<LogViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<MainViewModel>();

                // MainWindow
                services.AddSingleton<MainWindow>();
            })
            .Build();

        await _host.StartAsync();

        var window = _host.Services.GetRequiredService<MainWindow>();
        window.DataContext = _host.Services.GetRequiredService<MainViewModel>();
        window.Show();

        // Diagnose und Container-Liste initial automatisch starten — non-blocking,
        // im Background-Task. Fehler hier dürfen die App nicht killen. Ohne den
        // initialen Refresh würde Manage erst nach 10 s (Timer-Tick) Daten zeigen.
        _ = Task.Run(async () =>
        {
            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    var diag = _host.Services.GetRequiredService<DiagnosticsViewModel>();
                    if (diag.RunAllCommand.CanExecute(null))
                    {
                        diag.RunAllCommand.Execute(null);
                    }

                    var manage = _host.Services.GetRequiredService<ManageContainersViewModel>();
                    if (manage.RefreshCommand.CanExecute(null))
                    {
                        manage.RefreshCommand.Execute(null);
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Initiale Diagnose / Container-Liste fehlgeschlagen");
            }
        });
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
            // PowerShell-Runner sauber abräumen — Temp-Param-Files etc.
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
            Log.Information("BC Container Creator beendet");
            await Log.CloseAndFlushAsync();
        }
        base.OnExit(e);
    }
}
