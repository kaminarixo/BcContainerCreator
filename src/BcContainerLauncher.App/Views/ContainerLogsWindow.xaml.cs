using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using BcContainerLauncher.App.ViewModels;

namespace BcContainerLauncher.App.Views;

public partial class ContainerLogsWindow : Window
{
    public ContainerLogsWindow(ContainerLogsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
        viewModel.PropertyChanged += OnVmPropertyChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ContainerLogsViewModel vm)
        {
            await vm.LoadAsync();
            ScrollLogToEnd();
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ContainerLogsViewModel.Logs))
        {
            // Nach Refresh ans Ende scrollen — sonst sieht man die jüngsten
            // Zeilen nur, wenn man manuell scrollt.
            ScrollLogToEnd();
        }
    }

    private void ScrollLogToEnd()
    {
        Dispatcher.BeginInvoke(new Action(() => LogText.ScrollToEnd()), System.Windows.Threading.DispatcherPriority.Background);
    }
}
