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
    /// Liste der wählbaren Versionen: "latest" + die letzten N BC-Major-Releases,
    /// jeweils mit dem konkret aufgelösten neuesten Build.
    /// </summary>
    public ObservableCollection<ArtifactVersionOption> AvailableVersions { get; } =
        new() { new ArtifactVersionOption(LatestVersionToken, null) };

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
    private ArtifactVersionOption _selectedVersion = new(LatestVersionToken, null);

    /// <summary>
    /// Anzeige der konkret aufgelösten Version der aktuellen Auswahl
    /// (z. B. "28.0.46665.49591"). Aktualisiert sich live mit
    /// <see cref="SelectedVersion"/>.
    /// </summary>
    public string? ResolvedBuild => SelectedVersion?.LatestBuild;

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
    private bool _includeTestToolkit;

    [ObservableProperty]
    private bool _multitenant;

    public IReadOnlyList<string> IsolationModes { get; } = new[] { "(Standard)", "process", "hyperv" };

    [ObservableProperty]
    private string _selectedIsolation = "(Standard)";

    /// <summary>z. B. "8G", "16G". Leer = kein Limit.</summary>
    [ObservableProperty]
    private string _memoryLimit = string.Empty;

    [ObservableProperty]
    private bool _showAdvanced;

    [ObservableProperty]
    private bool _isRunning;

    /// <summary>Hilfs-Property für IsEnabled-Bindings — invertiert <see cref="IsRunning"/>.</summary>
    public bool IsCreateFormEnabled => !IsRunning;

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
            var options = await _containerService.GetVersionOptionsAsync(SelectedArtifactType, SelectedCountry, topMajors: 6, ct);
            ct.ThrowIfCancellationRequested();

            var prevSelector = SelectedVersion?.Selector;
            AvailableVersions.Clear();
            foreach (var opt in options)
            {
                AvailableVersions.Add(opt);
            }
            if (AvailableVersions.Count == 0)
            {
                AvailableVersions.Add(new ArtifactVersionOption(LatestVersionToken, null));
            }

            // Bisherige Auswahl beibehalten, falls noch da; sonst auf 'latest'.
            var keep = AvailableVersions.FirstOrDefault(o => string.Equals(o.Selector, prevSelector, StringComparison.OrdinalIgnoreCase));
            SelectedVersion = keep ?? AvailableVersions[0];
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

    partial void OnSelectedVersionChanged(ArtifactVersionOption value) => OnPropertyChanged(nameof(ResolvedBuild));

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
            var isolation = string.Equals(SelectedIsolation, "(Standard)", StringComparison.OrdinalIgnoreCase)
                ? null
                : SelectedIsolation;

            var request = new ContainerCreateRequest(
                ContainerName: ContainerName,
                ArtifactType: SelectedArtifactType,
                Country: SelectedCountry,
                Version: SelectedVersion?.Selector ?? LatestVersionToken,
                AuthType: SelectedAuthType,
                Username: Username,
                Password: ToSecureString(Password),
                LicenseFilePath: string.IsNullOrWhiteSpace(LicenseFilePath) ? null : LicenseFilePath,
                AcceptEula: AcceptEula,
                IncludeAL: IncludeAL,
                IncludeTestToolkit: IncludeTestToolkit,
                MemoryLimit: string.IsNullOrWhiteSpace(MemoryLimit) ? null : MemoryLimit.Trim(),
                Isolation: isolation,
                Multitenant: Multitenant);

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
        && SelectedVersion is not null
        && (SelectedAuthType == AuthType.Windows || (!string.IsNullOrEmpty(Password) && !string.IsNullOrWhiteSpace(Username)));

    private bool CanCancel() => IsRunning;

    partial void OnIsRunningChanged(bool value)
    {
        CancelCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsCreateFormEnabled));
    }

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
