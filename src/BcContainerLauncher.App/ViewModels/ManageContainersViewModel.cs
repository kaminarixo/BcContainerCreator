using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using BcContainerLauncher.App.Services;
using BcContainerLauncher.Core.Containers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BcContainerLauncher.App.ViewModels;

/// <summary>
/// ViewModel für den Container-verwalten-Tab. Liefert Liste, Start, Stop,
/// Löschen, URL-Open. Refresh kann manuell oder automatisch nach jeder Aktion.
/// </summary>
public sealed partial class ManageContainersViewModel : ObservableObject
{
    private readonly IContainerService _containerService;
    private readonly IDialogService _dialogService;
    private readonly ILogger<ManageContainersViewModel> _logger;

    public ObservableCollection<ContainerInfoViewModel> Containers { get; } = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusText = "Bereit. Klick 'Aktualisieren' um Container zu laden.";

    public ManageContainersViewModel(
        IContainerService containerService,
        IDialogService dialogService,
        ILogger<ManageContainersViewModel> logger)
    {
        _containerService = containerService;
        _dialogService = dialogService;
        _logger = logger;
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        StatusText = "Lade Container…";
        try
        {
            var list = await _containerService.ListContainersAsync();

            // Map über Container-Name, damit IsBusy nicht verloren geht.
            var existing = Containers.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
            Containers.Clear();
            foreach (var info in list.OrderByDescending(c => c.IsRunning).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!existing.TryGetValue(info.Name, out var vm))
                {
                    vm = new ContainerInfoViewModel();
                }
                vm.Apply(info);
                Containers.Add(vm);
            }

            StatusText = $"{Containers.Count} Container gefunden ({Containers.Count(c => c.IsRunning)} laufend).";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Container-List fehlgeschlagen");
            StatusText = $"Fehler: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task StartAsync(ContainerInfoViewModel? vm)
    {
        if (vm is null) return;
        await ExecuteAction(vm, "Starten", _ => _containerService.StartContainerAsync(vm.Name));
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task StopAsync(ContainerInfoViewModel? vm)
    {
        if (vm is null) return;
        await ExecuteAction(vm, "Stoppen", _ => _containerService.StopContainerAsync(vm.Name));
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task RemoveAsync(ContainerInfoViewModel? vm)
    {
        if (vm is null) return;

        var ok = MessageBox.Show(
            $"Container '{vm.Name}' wirklich löschen?\n\nDieser Vorgang kann nicht rückgängig gemacht werden.",
            "Container löschen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (ok != MessageBoxResult.Yes) return;

        await ExecuteAction(vm, "Löschen", _ => _containerService.RemoveContainerAsync(vm.Name, force: true));
    }

    [RelayCommand]
    private void OpenUrl(ContainerInfoViewModel? vm)
    {
        if (vm?.WebClientUrl is null) return;
        try
        {
            Process.Start(new ProcessStartInfo(vm.WebClientUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "URL öffnen fehlgeschlagen");
            _dialogService.ShowMessage($"URL konnte nicht geöffnet werden: {ex.Message}", "Fehler", isError: true);
        }
    }

    private async Task ExecuteAction(ContainerInfoViewModel vm, string label, Func<CancellationToken, Task<bool>> action)
    {
        if (vm.IsBusy) return;
        vm.IsBusy = true;
        StatusText = $"{label} '{vm.Name}'…";
        try
        {
            var ok = await action(CancellationToken.None);
            if (!ok)
            {
                _dialogService.ShowMessage(
                    $"{label} von '{vm.Name}' fehlgeschlagen — siehe Log-Tab.",
                    "Aktion fehlgeschlagen",
                    isError: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Label} '{Name}' geworfen", label, vm.Name);
            _dialogService.ShowMessage(ex.Message, "Fehler", isError: true);
        }
        finally
        {
            vm.IsBusy = false;
            // Nach jeder Aktion: Refresh, damit der Status korrekt ist.
            await RefreshAsync();
        }
    }

    private bool CanRefresh() => !IsLoading;
    private bool CanAct(ContainerInfoViewModel? vm) => vm is { IsBusy: false } && !IsLoading;

    partial void OnIsLoadingChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
    }
}
