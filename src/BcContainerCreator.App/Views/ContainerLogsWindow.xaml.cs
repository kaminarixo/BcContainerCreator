using System.ComponentModel;
using System.Windows;
using BcContainerCreator.App.ViewModels;

namespace BcContainerCreator.App.Views;

public partial class ContainerLogsWindow : Window
{
    public ContainerLogsWindow(ContainerLogsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
        viewModel.PropertyChanged += OnVmPropertyChanged;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // async void: Exceptions hier würden ungefangen im Dispatcher landen.
        // LoadAsync fängt selbst, der Guard deckt den Rest (z. B. Scroll) ab.
        try
        {
            if (DataContext is ContainerLogsViewModel vm)
            {
                await vm.LoadAsync();
                ScrollLogToEnd();
            }
        }
        catch
        {
            // Fehler zeigt das ViewModel über StatusText an.
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        // Symmetrisch zu den Subscriptions im Konstruktor abhängen.
        if (DataContext is ContainerLogsViewModel vm)
        {
            vm.PropertyChanged -= OnVmPropertyChanged;
        }
        Loaded -= OnLoaded;
        Closed -= OnClosed;
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
