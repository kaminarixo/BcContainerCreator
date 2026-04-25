using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BcContainerLauncher.Core.Setup;

namespace BcContainerLauncher.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        TryLoadBrandAssets();
        ApplyContextBadge();
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
        var logo = TryLoadAsset("pack://application:,,,/Assets/logo.png");
        if (logo is not null)
        {
            Icon = logo;
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
