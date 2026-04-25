using System.Collections.ObjectModel;
using BcContainerLauncher.App.Services;
using BcContainerLauncher.Core.Setup;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BcContainerLauncher.App.ViewModels;

/// <summary>
/// ViewModel für den Diagnose-Tab. Lädt initial alle Checks und hält
/// pro Check ein <see cref="CheckResultViewModel"/>.
/// </summary>
public sealed partial class DiagnosticsViewModel : ObservableObject
{
    private readonly IPreflightCheck _preflight;
    private readonly ISetupService _setup;
    private readonly ILogger<DiagnosticsViewModel> _logger;
    private CancellationTokenSource? _cts;

    public ObservableCollection<CheckResultViewModel> Checks { get; } = new();

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _statusText = "Bereit.";

    public DiagnosticsViewModel(IPreflightCheck preflight, ISetupService setup, ILogger<DiagnosticsViewModel> logger)
    {
        _preflight = preflight;
        _setup = setup;
        _logger = logger;
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAllAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsRunning = true;
        StatusText = "Prüfe Voraussetzungen…";
        Checks.Clear();
        RunAllCommand.NotifyCanExecuteChanged();
        FixAllCommand.NotifyCanExecuteChanged();

        try
        {
            // Map über CheckId, damit wiederholte Läufe dieselbe Row aktualisieren.
            var byName = new Dictionary<string, CheckResultViewModel>(StringComparer.Ordinal);
            var progress = new DispatcherProgress<Core.Models.CheckResult>(r =>
            {
                if (!byName.TryGetValue(r.Name, out var vm))
                {
                    vm = new CheckResultViewModel();
                    byName[r.Name] = vm;
                    Checks.Add(vm);
                }
                vm.Apply(r);
            });

            await _preflight.RunAllAsync(progress, _cts.Token);
            StatusText = $"Fertig. {Checks.Count(c => c.Status == Core.Models.CheckStatus.Ok)}/{Checks.Count} OK.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Abgebrochen.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Diagnose fehlgeschlagen");
            StatusText = $"Fehler: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
            RunAllCommand.NotifyCanExecuteChanged();
            FixAllCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanFix))]
    private async Task FixAsync(CheckResultViewModel? vm)
    {
        if (vm?.FixId is null || vm.IsFixing)
        {
            return;
        }

        vm.IsFixing = true;
        try
        {
            var ok = await _setup.ApplyFixAsync(vm.FixId);
            StatusText = ok
                ? $"Fix '{vm.Name}' angewendet — bitte neu prüfen."
                : $"Fix '{vm.Name}' fehlgeschlagen.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fix {FixId} geworfen", vm.FixId);
            StatusText = $"Fehler: {ex.Message}";
        }
        finally
        {
            vm.IsFixing = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanFixAll))]
    private async Task FixAllAsync()
    {
        var fixable = Checks.Where(c => c.IsFixable && !string.IsNullOrEmpty(c.FixId)).ToList();
        foreach (var vm in fixable)
        {
            await FixAsync(vm);
        }
        StatusText = $"{fixable.Count} Fix(es) versucht. Bitte 'Alle prüfen' erneut ausführen.";
    }

    private bool CanRun() => !IsRunning;
    private bool CanFix(CheckResultViewModel? vm) => !IsRunning && vm is { IsFixable: true, IsFixing: false };
    private bool CanFixAll() => !IsRunning && Checks.Any(c => c.IsFixable);

    partial void OnIsRunningChanged(bool value)
    {
        FixCommand.NotifyCanExecuteChanged();
    }
}
