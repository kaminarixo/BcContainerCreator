using System.Windows;
using BcContainerLauncher.App.ViewModels;

namespace BcContainerLauncher.App.Views;

public partial class ContainerInfoWindow : Window
{
    public ContainerInfoWindow(ContainerCredentialsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
