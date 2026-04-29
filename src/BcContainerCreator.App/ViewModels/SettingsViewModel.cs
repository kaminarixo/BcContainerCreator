using System.Diagnostics;
using System.IO;
using System.Reflection;
using BcContainerCreator.App.Services;
using BcContainerCreator.Core.Setup;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BcContainerCreator.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IDialogService _dialogService;
    private readonly ILogger<SettingsViewModel> _logger;

    public string AppVersion { get; }
    public string DotNetVersion { get; } = Environment.Version.ToString();

    /// <summary>
    /// Anzeigetext fürs OS. Achtung: <see cref="Environment.OSVersion"/>
    /// liefert auf Windows 11 weiterhin "Microsoft Windows NT 10.0…" (das ist
    /// historisch eingefroren). Korrekte 10-vs-11-Unterscheidung geht nur über
    /// die Build-Number — gleicher Trick wie im Preflight-Check.
    /// </summary>
    public string OsVersion { get; } = BuildOsVersionLabel();

    public string LogFolder { get; }
    public string ContextLabel { get; } =
        AdminContext.IsCurrentProcessAdmin ? "Admin-Modus" : "Standard-User (Admin via UAC on demand)";

    public SettingsViewModel(IDialogService dialogService, ILogger<SettingsViewModel> logger)
    {
        _dialogService = dialogService;
        _logger = logger;

        var asm = Assembly.GetExecutingAssembly();
        var ver = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                  ?? asm.GetName().Version?.ToString()
                  ?? "unbekannt";
        AppVersion = ver;

        LogFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "BcContainerCreator", "Logs");
    }

    private static string BuildOsVersionLabel()
    {
        var v = Environment.OSVersion.Version;
        // Build-Number 22000 markiert den Sprung 10 → 11. Server-Builds (z. B.
        // 20348 = Server 2022) bekommen weiterhin "Windows 10/Server 10".
        var product = v.Build >= 22000 ? "Windows 11" : "Windows 10";
        return $"{product} (Build {v.Build}.{v.Revision})";
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            if (!Directory.Exists(LogFolder))
            {
                Directory.CreateDirectory(LogFolder);
            }
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{LogFolder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Log-Ordner öffnen fehlgeschlagen");
            _dialogService.ShowMessage(ex.Message, "Fehler", isError: true);
        }
    }

}
