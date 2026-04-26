using System.Windows;
using BcContainerCreator.App.ViewModels;

namespace BcContainerCreator.App.Views;

public partial class ContainerInfoWindow : Window
{
    public ContainerInfoWindow(ContainerCredentialsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
