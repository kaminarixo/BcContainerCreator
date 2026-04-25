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
/// ViewModel des "Container erstellen"-Tabs. Validierung über DataAnnotations.
/// Versions-Liste und "latest"-Auflösung kommen aus dem ContainerService;
/// Passwort wird als Plain-String gehalten (Show/Hide-Toggle) und beim
/// Submit in einen <see cref="SecureString"/> konvertiert.
/// </summary>
public sealed partial class CreateContainerViewModel : ObservableValidator
{
    private const string LatestVersionToken = "latest";
    private const string DefaultPassword = "P@ssw0rd1";

    private readonly IContainerService _containerService;
    private readonly IDialogService _dialogService;
    private readonly ILogger<CreateContainerViewModel> _logger;
    private CancellationTokenSource? _createCts;
    private CancellationTokenSource? _versionsCts;

    public ObservableCollection<string> Countries { get; } =
        new(Constants.SupportedCountries);

    public IReadOnlyList<ArtifactType> ArtifactTypes { get; } =
        new[] { ArtifactType.OnPrem, ArtifactType.Sandbox };

    public IReadOnlyList<AuthType> AuthTypes { get; } =
        new[] { AuthType.NavUserPassword, AuthType.Windows };

    /// <summary>
    /// Liste der verfügbaren Versionen — "latest" steht immer ganz oben,
    /// danach die letzten X konkret aufgelösten Versionen.
    /// </summary>
    public ObservableCollection<string> AvailableVersions { get; } =
        new() { LatestVersionToken };

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
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _selectedVersion = LatestVersionToken;

    /// <summary>Anzeige der konkret aufgelösten "latest"-Version, z. B. "26.0.1234.5678".</summary>
    [ObservableProperty]
    private string? _latestResolvedVersion;

    [ObservableProperty]
    private bool _isLoadingVersions;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private AuthType _selectedAuthType = AuthType.NavUserPassword;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(AllowEmptyStrings = false)]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _username = Environment.UserName;

    /// <summary>Passwort als Plain-String — beim Submit zu SecureString.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _password = DefaultPassword;

    [ObservableProperty]
    private bool _showPassword;

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

    /// <summary>
    /// Lädt im Hintergrund Versionen + Latest-Auflösung. Fire-and-forget,
    /// Fehler dürfen die UI nicht blockieren — der User kann "latest" notfalls
    /// auch ohne aufgelöste Anzeige verwenden.
    /// </summary>
    public async Task RefreshVersionsAsync()
    {
        _versionsCts?.Cancel();
        _versionsCts = new CancellationTokenSource();
        var ct = _versionsCts.Token;

        IsLoadingVersions = true;
        try
        {
            var versionsTask = _containerService.GetAvailableVersionsAsync(SelectedArtifactType, SelectedCountry, top: 12, ct);
            var latestTask = _containerService.ResolveLatestVersionAsync(SelectedArtifactType, SelectedCountry, ct);

            var versions = await versionsTask;
            var latest = await latestTask;

            ct.ThrowIfCancellationRequested();

            // "latest" bleibt immer als erster Eintrag, dann die konkreten Versionen.
            AvailableVersions.Clear();
            AvailableVersions.Add(LatestVersionToken);
            foreach (var v in versions)
            {
                AvailableVersions.Add(v);
            }

            LatestResolvedVersion = latest;

            if (!AvailableVersions.Contains(SelectedVersion))
            {
                SelectedVersion = LatestVersionToken;
            }
        }
        catch (OperationCanceledException) { /* refresh-superseded */ }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RefreshVersionsAsync fehlgeschlagen");
        }
        finally
        {
            IsLoadingVersions = false;
        }
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

    [RelayCommand]
    private Task ReloadVersions() => RefreshVersionsAsync();

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        ValidateAllProperties();
        if (HasErrors)
        {
            _dialogService.ShowMessage("Bitte alle markierten Felder korrigieren.", "Validierung", isError: true);
            return;
        }

        if (SelectedAuthType == AuthType.NavUserPassword && string.IsNullOrEmpty(Password))
        {
            _dialogService.ShowMessage("Passwort ist erforderlich für NavUserPassword.", "Validierung", isError: true);
            return;
        }

        _createCts?.Cancel();
        _createCts = new CancellationTokenSource();
        IsRunning = true;
        Output = string.Empty;

        try
        {
            var request = new ContainerCreateRequest(
                ContainerName: ContainerName,
                ArtifactType: SelectedArtifactType,
                Country: SelectedCountry,
                Version: SelectedVersion,
                AuthType: SelectedAuthType,
                Username: Username,
                Password: ToSecureString(Password),
                LicenseFilePath: string.IsNullOrWhiteSpace(LicenseFilePath) ? null : LicenseFilePath,
                AcceptEula: AcceptEula,
                IncludeAL: IncludeAL);

            var progress = new DispatcherProgress<string>(line =>
            {
                Output += line + Environment.NewLine;
            });

            var result = await _containerService.CreateContainerAsync(request, progress, _createCts.Token);
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
    private void Cancel() => _createCts?.Cancel();

    private bool CanCreate() =>
        !IsRunning
        && !HasErrors
        && !string.IsNullOrWhiteSpace(ContainerName)
        && !string.IsNullOrWhiteSpace(SelectedCountry)
        && !string.IsNullOrWhiteSpace(SelectedVersion)
        && (SelectedAuthType == AuthType.Windows || (!string.IsNullOrEmpty(Password) && !string.IsNullOrWhiteSpace(Username)));

    private bool CanCancel() => IsRunning;

    partial void OnIsRunningChanged(bool value) => CancelCommand.NotifyCanExecuteChanged();

    // Versions-Liste neu laden, wenn ArtifactType oder Country wechselt.
    partial void OnSelectedArtifactTypeChanged(ArtifactType value) => _ = RefreshVersionsAsync();
    partial void OnSelectedCountryChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _ = RefreshVersionsAsync();
        }
    }

    private static SecureString ToSecureString(string plain)
    {
        var ss = new SecureString();
        foreach (var c in plain)
        {
            ss.AppendChar(c);
        }
        ss.MakeReadOnly();
        return ss;
    }
}
