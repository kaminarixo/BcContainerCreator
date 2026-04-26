using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using BcContainerLauncher.App.Services;
using BcContainerLauncher.App.Views;
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
    private readonly IContainerMetadataStore _metadata;
    private readonly IDialogService _dialogService;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ManageContainersViewModel> _logger;
    private readonly DispatcherTimer _autoRefreshTimer;

    public ObservableCollection<ContainerInfoViewModel> Containers { get; } = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusText = "Bereit. Klick 'Aktualisieren' um Container zu laden.";

    [ObservableProperty]
    private bool _autoRefreshEnabled = true;

    public ManageContainersViewModel(
        IContainerService containerService,
        IContainerMetadataStore metadata,
        IDialogService dialogService,
        ILoggerFactory loggerFactory)
    {
        _containerService = containerService;
        _metadata = metadata;
        _dialogService = dialogService;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<ManageContainersViewModel>();

        // Alle 10 s neu laden — der Tick wird übersprungen, wenn gerade
        // schon eine Aktion läuft, damit der User nicht in eine Liste klickt,
        // die sich gerade unter ihm wegmodifiziert.
        _autoRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _autoRefreshTimer.Tick += OnAutoRefreshTick;
        _autoRefreshTimer.Start();
    }

    private async void OnAutoRefreshTick(object? sender, EventArgs e)
    {
        if (!AutoRefreshEnabled) return;
        if (IsLoading) return;
        if (Containers.Any(c => c.IsBusy)) return;
        try
        {
            await RefreshAsync();
        }
        catch
        {
            // Tick-Fehler dürfen die Timer-Kette nicht killen.
        }
    }

    partial void OnAutoRefreshEnabledChanged(bool value)
    {
        if (value && !_autoRefreshTimer.IsEnabled)
        {
            _autoRefreshTimer.Start();
        }
        else if (!value && _autoRefreshTimer.IsEnabled)
        {
            _autoRefreshTimer.Stop();
        }
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

    [RelayCommand]
    private async Task ShowInfoAsync(ContainerInfoViewModel? vm)
    {
        if (vm is null) return;
        try
        {
            var meta = await _metadata.LoadAsync(vm.Name);
            var pwPlain = meta is null ? null : _metadata.DecryptPassword(meta.PasswordCipher);

            var infoVm = new ContainerCredentialsViewModel(vm.Name, meta, pwPlain);
            var window = new ContainerInfoWindow(infoVm)
            {
                Owner = Application.Current?.MainWindow
            };
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Info-Window konnte nicht geöffnet werden");
            _dialogService.ShowMessage(ex.Message, "Fehler", isError: true);
        }
    }

    [RelayCommand]
    private void ShowLogs(ContainerInfoViewModel? vm)
    {
        if (vm is null) return;
        try
        {
            var logsVm = new ContainerLogsViewModel(
                vm.Name,
                _containerService,
                _loggerFactory.CreateLogger<ContainerLogsViewModel>());
            var window = new ContainerLogsWindow(logsVm)
            {
                Owner = Application.Current?.MainWindow
            };
            window.Show();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Logs-Window konnte nicht geöffnet werden");
            _dialogService.ShowMessage(ex.Message, "Fehler", isError: true);
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
