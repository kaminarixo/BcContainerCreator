using System.Collections.ObjectModel;
using BcContainerLauncher.Core.Containers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BcContainerLauncher.App.ViewModels;

public sealed partial class ContainerLogsViewModel : ObservableObject
{
    private readonly IContainerService _containerService;
    private readonly ILogger<ContainerLogsViewModel> _logger;

    public string ContainerName { get; }
    public ObservableCollection<int> TailOptions { get; } = new() { 100, 500, 1000, 5000, 10000 };

    [ObservableProperty]
    private int _tail = 1000;

    [ObservableProperty]
    private string _logs = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusText = "Bereit.";

    public ContainerLogsViewModel(
        string containerName,
        IContainerService containerService,
        ILogger<ContainerLogsViewModel> logger)
    {
        ContainerName = containerName;
        _containerService = containerService;
        _logger = logger;
    }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusText = $"Lade letzte {Tail} Zeilen…";
        try
        {
            var text = await _containerService.GetContainerLogsAsync(ContainerName, Tail);
            Logs = text;
            var lineCount = string.IsNullOrEmpty(text) ? 0 : text.Split('\n').Length;
            StatusText = $"{lineCount} Zeilen geladen.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Logs laden fehlgeschlagen für {Name}", ContainerName);
            StatusText = $"Fehler: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanLoad() => !IsLoading;

    partial void OnIsLoadingChanged(bool value) => LoadCommand.NotifyCanExecuteChanged();
}
