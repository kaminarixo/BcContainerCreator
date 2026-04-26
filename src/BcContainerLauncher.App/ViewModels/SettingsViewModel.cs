using System.Diagnostics;
using System.IO;
using System.Reflection;
using BcContainerLauncher.App.Services;
using BcContainerLauncher.Core.Setup;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BcContainerLauncher.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private const string GitHubUrl = "https://github.com/kaminarixo/BcContainerCreator";

    private readonly IDialogService _dialogService;
    private readonly ILogger<SettingsViewModel> _logger;

    public string AppVersion { get; }
    public string DotNetVersion { get; } = Environment.Version.ToString();
    public string OsVersion { get; } = Environment.OSVersion.ToString();
    public string LogFolder { get; }
    public string ContextLabel { get; } =
        AdminContext.IsCurrentProcessAdmin ? "Admin-Modus" : "Standard-User (Admin via UAC on demand)";
    public string GitHubUrlDisplay => GitHubUrl;

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
            "BcContainerLauncher", "Logs");
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

    [RelayCommand]
    private void OpenGitHub()
    {
        try
        {
            Process.Start(new ProcessStartInfo(GitHubUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GitHub-Link öffnen fehlgeschlagen");
            _dialogService.ShowMessage(ex.Message, "Fehler", isError: true);
        }
    }
}
