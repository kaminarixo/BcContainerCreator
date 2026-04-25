using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Security;
using BcContainerLauncher.App.Services;
using BcContainerLauncher.Core;
using BcContainerLauncher.Core.Containers;
using BcContainerLauncher.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BcContainerLauncher.App.ViewModels;

/// <summary>
/// ViewModel des "Container erstellen"-Tabs. Validierung über DataAnnotations
/// (CommunityToolkit.Mvvm <see cref="ObservableValidator"/>).
/// </summary>
public sealed partial class CreateContainerViewModel : ObservableValidator
{
    private readonly IContainerService _containerService;
    private readonly IDialogService _dialogService;
    private readonly ILogger<CreateContainerViewModel> _logger;
    private CancellationTokenSource? _cts;

    public ObservableCollection<string> Countries { get; } =
        new(Constants.SupportedCountries);

    public IReadOnlyList<ArtifactType> ArtifactTypes { get; } =
        new[] { ArtifactType.OnPrem, ArtifactType.Sandbox };

    public IReadOnlyList<AuthType> AuthTypes { get; } =
        new[] { AuthType.NavUserPassword, AuthType.Windows };

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Containername ist erforderlich.")]
    [RegularExpression("^[A-Za-z0-9_-]+$", ErrorMessage = "Nur a-z, A-Z, 0-9, -, _ erlaubt.")]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _containerName = "bcdev";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private ArtifactType _selectedArtifactType = ArtifactType.OnPrem;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _selectedCountry = Constants.DefaultCountry;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _version = Constants.DefaultVersion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private AuthType _selectedAuthType = AuthType.NavUserPassword;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(AllowEmptyStrings = false)]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _username = "admin";

    /// <summary>An den PasswordBox via PasswordBoxAssistant gebunden.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private SecureString? _password;

    [ObservableProperty]
    private string? _licenseFilePath;

    [ObservableProperty]
    private bool _acceptEula = true;

    [ObservableProperty]
    private bool _includeAL = true;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _output = string.Empty;

    public CreateContainerViewModel(
        IContainerService containerService,
        IDialogService dialogService,
        ILogger<CreateContainerViewModel> logger)
    {
        _containerService = containerService;
        _dialogService = dialogService;
        _logger = logger;
    }

    [RelayCommand]
    private void PickLicenseFile()
    {
        var path = _dialogService.PickFile(
            "BC-Lizenz (*.flf;*.bclicense)|*.flf;*.bclicense|Alle Dateien|*.*",
            "BC-Lizenz wählen");
        if (path is not null)
        {
            LicenseFilePath = path;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        ValidateAllProperties();
        if (HasErrors)
        {
            _dialogService.ShowMessage("Bitte alle markierten Felder korrigieren.", "Validierung", isError: true);
            return;
        }

        if (SelectedAuthType == AuthType.NavUserPassword && (Password is null || Password.Length == 0))
        {
            _dialogService.ShowMessage("Passwort ist erforderlich für NavUserPassword.", "Validierung", isError: true);
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsRunning = true;
        Output = string.Empty;

        try
        {
            var request = new ContainerCreateRequest(
                ContainerName: ContainerName,
                ArtifactType: SelectedArtifactType,
                Country: SelectedCountry,
                Version: Version,
                AuthType: SelectedAuthType,
                Username: Username,
                Password: Password ?? new SecureString(),
                LicenseFilePath: string.IsNullOrWhiteSpace(LicenseFilePath) ? null : LicenseFilePath,
                AcceptEula: AcceptEula,
                IncludeAL: IncludeAL);

            var progress = new DispatcherProgress<string>(line =>
            {
                Output += line + Environment.NewLine;
            });

            var result = await _containerService.CreateContainerAsync(request, progress, _cts.Token);
            if (result.WasCancelled)
            {
                _dialogService.ShowMessage("Erstellung abgebrochen.", "Abgebrochen");
            }
            else if (result.Success)
            {
                _dialogService.ShowMessage(
                    $"Container '{ContainerName}' erstellt.\nDauer: {result.Duration:mm\\:ss}",
                    "Erfolg");
            }
            else
            {
                _dialogService.ShowMessage(
                    "Erstellung fehlgeschlagen:\n\n" + string.Join("\n", result.Errors),
                    "Fehler",
                    isError: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Create-Container geworfen");
            _dialogService.ShowMessage(ex.Message, "Fehler", isError: true);
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cts?.Cancel();
    }

    private bool CanCreate() =>
        !IsRunning
        && !HasErrors
        && !string.IsNullOrWhiteSpace(ContainerName)
        && !string.IsNullOrWhiteSpace(SelectedCountry)
        && !string.IsNullOrWhiteSpace(Version)
        && (SelectedAuthType == AuthType.Windows || (Password is not null && Password.Length > 0 && !string.IsNullOrWhiteSpace(Username)));

    private bool CanCancel() => IsRunning;

    partial void OnIsRunningChanged(bool value)
    {
        CancelCommand.NotifyCanExecuteChanged();
    }
}
