using BcContainerCreator.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BcContainerCreator.App.ViewModels;

/// <summary>
/// View-Wrapper um <see cref="ContainerInfo"/>. <see cref="IsBusy"/> verhindert,
/// dass der gleiche Container parallel start/stop/delete bekommt.
/// </summary>
public sealed partial class ContainerInfoViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _image = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string? _webClientUrl;

    [ObservableProperty]
    private bool _isBcContainer;

    [ObservableProperty]
    private bool _isBusy;

    public void Apply(ContainerInfo info)
    {
        Name = info.Name;
        Image = info.Image;
        Status = info.Status;
        IsRunning = info.IsRunning;
        WebClientUrl = info.WebClientUrl;
        IsBcContainer = info.IsBcContainer;
    }
}
