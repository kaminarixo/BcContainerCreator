using System.ComponentModel;
using System.Reflection;
using System.Windows;
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
        ApplyHeaderInfo();
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
            // externe powershell.exe-Subprozess sauber abgeräumt wird.
            if (vm.CreateContainer.CancelCommand.CanExecute(null))
            {
                vm.CreateContainer.CancelCommand.Execute(null);
            }
        }
    }

    /// <summary>
    /// Setzt Title-Bar-Suffix, Brand-Block-Version und User-Pill basierend
    /// auf Ausführungs-Kontext und Assembly-Version.
    /// </summary>
    private void ApplyHeaderInfo()
    {
        var contextSuffix = AdminContext.IsCurrentProcessAdmin ? "Admin-Modus" : "Standard-User";
        TitleText.Text = $"BC Container Creator — {contextSuffix}";

        // Domänen-Konto als DOMAIN\User anzeigen; bei lokalen Konten ist die
        // "Domäne" der Rechnername — dann reicht der Username.
        var isDomainAccount = !string.Equals(
            Environment.UserDomainName, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
        UserNameLabel.Text = isDomainAccount
            ? $@"{Environment.UserDomainName}\{Environment.UserName}"
            : Environment.UserName;
        UserNameLabel.ToolTip =
            "Angemeldetes Windows-Konto. Gespeicherte Container-Passwörter sind per DPAPI an dieses Konto gebunden.";

        var asm = Assembly.GetExecutingAssembly();
        var ver = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                  ?? asm.GetName().Version?.ToString(3)
                  ?? "1.0.0";
        VersionLabel.Text = "v" + ver;
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
