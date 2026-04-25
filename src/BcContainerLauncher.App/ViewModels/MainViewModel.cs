using CommunityToolkit.Mvvm.ComponentModel;

namespace BcContainerLauncher.App.ViewModels;

/// <summary>
/// Top-level ViewModel — bündelt die Tab-ViewModels.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    public DiagnosticsViewModel Diagnostics { get; }
    public CreateContainerViewModel CreateContainer { get; }
    public LogViewModel Log { get; }

    [ObservableProperty]
    private int _selectedTabIndex;

    public MainViewModel(
        DiagnosticsViewModel diagnostics,
        CreateContainerViewModel createContainer,
        LogViewModel log)
    {
        Diagnostics = diagnostics;
        CreateContainer = createContainer;
        Log = log;
    }
}
