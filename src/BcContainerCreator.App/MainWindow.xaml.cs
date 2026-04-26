using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BcContainerCreator.App.ViewModels;
using BcContainerCreator.Core.Setup;

namespace BcContainerCreator.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        TryLoadBrandAssets();
        ApplyContextBadge();
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (vm.CreateContainer.IsRunning)
        {
            var result = MessageBox.Show(
                this,
                "Eine Container-Erstellung läuft aktuell.\n\n" +
                "Wenn du jetzt schließt, wird sie abgebrochen — ein eventuell halb erzeugter Docker-Container bleibt zurück und muss ggf. manuell entfernt werden.\n\n" +
                "Trotzdem schließen?",
                "Erstellung läuft noch",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            // User hat Schließen bestätigt — Cancel-Token feuern, damit der
            // PowerShell-Runspace sauber abgeräumt wird.
            if (vm.CreateContainer.CancelCommand.CanExecute(null))
            {
                vm.CreateContainer.CancelCommand.Execute(null);
            }
        }
    }

    private void ApplyContextBadge()
    {
        if (AdminContext.IsCurrentProcessAdmin)
        {
            ContextLabel.Text = "Admin-Modus";
            ContextDot.Fill = new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x4F)); // grün
        }
        else
        {
            ContextLabel.Text = "Standard-User";
            ContextDot.Fill = new SolidColorBrush(Color.FromRgb(0xE6, 0xA8, 0x00)); // gelb
        }
    }

    /// <summary>
    /// Setzt Window-Icon und Header-Logo aus den Brand-Assets. Fehlende Datei
    /// (z. B. vor dem ersten Logo-Drop) darf den Window-Start nicht killen.
    /// </summary>
    private void TryLoadBrandAssets()
    {
        // Window.Icon: ICO bevorzugen — multi-resolution, scharf in Taskbar
        // / Alt-Tab / Title-Bar. Header-Logo bleibt das hochaufgelöste PNG.
        var icon = TryLoadAsset("pack://application:,,,/Assets/icon.ico")
                   ?? TryLoadAsset("pack://application:,,,/Assets/logo.png");
        if (icon is not null)
        {
            Icon = icon;
        }

        var logo = TryLoadAsset("pack://application:,,,/Assets/logo.png");
        if (logo is not null)
        {
            LogoImage.Source = logo;
        }
    }

    private static BitmapImage? TryLoadAsset(string packUri)
    {
        try
        {
            // Erst prüfen, ob die Resource im Assembly registriert ist —
            // BitmapImage wirft sonst beim Rendering, nicht im Konstruktor.
            var resource = Application.GetResourceStream(new Uri(packUri, UriKind.Absolute));
            if (resource is null)
            {
                return null;
            }

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(packUri, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}
