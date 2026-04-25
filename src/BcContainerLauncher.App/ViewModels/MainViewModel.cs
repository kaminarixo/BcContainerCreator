using CommunityToolkit.Mvvm.ComponentModel;

namespace BcContainerLauncher.App.ViewModels;

/// <summary>
/// Top-level ViewModel — bündelt die Tab-ViewModels.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    public DiagnosticsViewModel Diagnostics { get; }
    public CreateContainerViewModel CreateContainer { get; }
    public ManageContainersViewModel ManageContainers { get; }
    public LogViewModel Log { get; }

    [ObservableProperty]
    private int _selectedTabIndex;

    public MainViewModel(
        DiagnosticsViewModel diagnostics,
        CreateContainerViewModel createContainer,
        ManageContainersViewModel manageContainers,
        LogViewModel log)
    {
        Diagnostics = diagnostics;
        CreateContainer = createContainer;
        ManageContainers = manageContainers;
        Log = log;
    }
}
