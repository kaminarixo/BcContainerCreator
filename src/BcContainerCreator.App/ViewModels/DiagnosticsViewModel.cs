using System.Collections.ObjectModel;
using BcContainerCreator.App.Services;
using BcContainerCreator.Core.Setup;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BcContainerCreator.App.ViewModels;

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

        try
        {
            // Sammle alles im PreflightCheck und übernehme die Liste danach
            // atomic in die UI. Live-Reports waren rennanfällig: zwei kurz
            // hintereinander gestartete RunAlls überlappten und führten zu
            // 11 → 10 → 9 statt stabil 11.
            var results = await _preflight.RunAllAsync(progress: null, _cts.Token);

            Checks.Clear();
            foreach (var r in results)
            {
                var vm = new CheckResultViewModel();
                vm.Apply(r);
                Checks.Add(vm);
            }
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
        }
    }

    [RelayCommand(CanExecute = nameof(CanFix))]
    private async Task FixAsync(CheckResultViewModel? vm)
    {
        if (vm?.FixId is null || vm.IsFixing)
        {
            return;
        }

        IsRunning = true;
        vm.IsFixing = true;
        StatusText = $"Wende Fix '{vm.Name}' an … (kann beim Erst-Bootstrap einige Sekunden dauern)";
        try
        {
            var ok = await _setup.ApplyFixAsync(vm.FixId);
            StatusText = ok
                ? $"Fix '{vm.Name}' angewendet — bitte neu prüfen."
                : $"Fix '{vm.Name}' fehlgeschlagen — siehe Log-Tab.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fix {FixId} geworfen", vm.FixId);
            StatusText = $"Fehler: {ex.Message}";
        }
        finally
        {
            vm.IsFixing = false;
            IsRunning = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanFixAll))]
    private async Task FixAllAsync()
    {
        IsRunning = true;
        try
        {
            var fixable = Checks.Where(c => c.IsFixable && !string.IsNullOrEmpty(c.FixId)).ToList();
            int done = 0;
            foreach (var vm in fixable)
            {
                StatusText = $"Fix {++done}/{fixable.Count}: {vm.Name} …";
                vm.IsFixing = true;
                try
                {
                    var ok = await _setup.ApplyFixAsync(vm.FixId!);
                    if (!ok)
                    {
                        _logger.LogWarning("Fix {FixId} hat false zurückgegeben", vm.FixId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Fix {FixId} geworfen", vm.FixId);
                }
                finally
                {
                    vm.IsFixing = false;
                }
            }
            StatusText = $"{fixable.Count} Fix(es) versucht. Bitte 'Alle prüfen' erneut ausführen.";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private bool CanRun() => !IsRunning;
    private bool CanFix(CheckResultViewModel? vm) => !IsRunning && vm is { IsFixable: true, IsFixing: false };
    private bool CanFixAll() => !IsRunning && Checks.Any(c => c.IsFixable);

    partial void OnIsRunningChanged(bool value)
    {
        RunAllCommand.NotifyCanExecuteChanged();
        FixAllCommand.NotifyCanExecuteChanged();
        FixCommand.NotifyCanExecuteChanged();
    }
}
