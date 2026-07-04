using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Security;
using System.Text;
using BcContainerCreator.App.Services;
using BcContainerCreator.Core;
using BcContainerCreator.Core.Containers;
using BcContainerCreator.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BcContainerCreator.App.ViewModels;

/// <summary>
/// ViewModel des "Container erstellen"-Tabs. Validierung über DataAnnotations.
/// Versions-Liste und "latest"-Auflösung kommen aus dem ContainerService;
/// das Passwort wird intern als Plain-String gehalten (Show/Hide-Toggle in
/// der UI braucht ihn) und beim Submit in einen <see cref="SecureString"/>
/// konvertiert. Der String wird nirgendwo geloggt und nur an die ACL-
/// geschützte Param-Datei des PowerShellRunners weitergereicht.
/// </summary>
public sealed partial class CreateContainerViewModel : ObservableValidator
{
    private const string LatestVersionToken = "latest";

    /// <summary>
    /// Hartes Cap für den Live-Output, damit WPF beim Container-Pull (extrem
    /// viele Layer-Fortschritts-Zeilen) den TextBox-Visual-Tree nicht in einen
    /// Layout-Stack-Overflow treibt. Bei Überschreitung wird vom Anfang
    /// gekürzt.
    /// </summary>
    private const int OutputMaxChars = 200_000;

    private readonly IContainerService _containerService;
    private readonly IDialogService _dialogService;
    private readonly ILogger<CreateContainerViewModel> _logger;
    private readonly StringBuilder _outputBuffer = new();
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

    /// <summary>
    /// Passwort als Plain-String — beim Submit zu SecureString konvertiert.
    /// Default ist leer; ein hardcodiertes "P@ssw0rd1" hatten wir früher,
    /// das ist jetzt explizit weg, damit jede Erst-Erstellung einen bewussten
    /// User-Input erzwingt. Pflicht nur bei NavUserPassword — die Regel läuft
    /// über die INotifyDataErrorInfo-Pipeline (roter Rahmen), konsistent zu
    /// den übrigen Feldern.
    /// </summary>
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(CreateContainerViewModel), nameof(ValidatePasswordRequired))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _password = string.Empty;

    /// <summary>
    /// DataAnnotations-Hook: Passwort ist nur bei NavUserPassword Pflicht.
    /// Muss public static sein, damit <see cref="CustomValidationAttribute"/>
    /// die Methode findet.
    /// </summary>
    public static ValidationResult? ValidatePasswordRequired(string? password, ValidationContext context)
    {
        var vm = (CreateContainerViewModel)context.ObjectInstance;
        if (vm.SelectedAuthType == AuthType.NavUserPassword && string.IsNullOrEmpty(password))
        {
            return new ValidationResult("Passwort ist erforderlich für NavUserPassword.");
        }
        return ValidationResult.Success;
    }

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

    // ----- Fortschritts-Properties -----

    /// <summary>Aktueller Fortschritt 0..100. Monoton steigend während eines Runs.</summary>
    [ObservableProperty]
    private int _createProgressPercent;

    /// <summary>Frei-Text-Status unter der ProgressBar (z. B. "Container wird erstellt").</summary>
    [ObservableProperty]
    private string _createProgressText = "Bereit";

    /// <summary>
    /// True solange noch keine Stage erkannt wurde — dann läuft die Marquee-
    /// Animation. Default ist false, damit die ProgressBar im Idle still steht;
    /// wird beim Start eines Runs auf true gesetzt und beim Erkennen der ersten
    /// Stage bzw. beim Run-Ende wieder zurückgenommen.
    /// </summary>
    [ObservableProperty]
    private bool _isCreateProgressIndeterminate;

    /// <summary>
    /// Fortschritts-Modus für das Windows-Taskbar-Icon: Marquee bis zur ersten
    /// erkannten Stage, danach normaler Fortschritt, im Idle aus.
    /// </summary>
    public System.Windows.Shell.TaskbarItemProgressState TaskbarProgressState =>
        !IsRunning ? System.Windows.Shell.TaskbarItemProgressState.None
        : IsCreateProgressIndeterminate ? System.Windows.Shell.TaskbarItemProgressState.Indeterminate
        : System.Windows.Shell.TaskbarItemProgressState.Normal;

    /// <summary>Fortschritt 0..1 für das Windows-Taskbar-Icon.</summary>
    public double TaskbarProgressValue => CreateProgressPercent / 100d;

    partial void OnCreateProgressPercentChanged(int value) =>
        OnPropertyChanged(nameof(TaskbarProgressValue));

    partial void OnIsCreateProgressIndeterminateChanged(bool value) =>
        OnPropertyChanged(nameof(TaskbarProgressState));

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
        // Deckt auch die Auth-abhängige Passwort-Pflicht ab (CustomValidation).
        ValidateAllProperties();
        if (HasErrors)
        {
            _dialogService.ShowMessage("Bitte alle markierten Felder korrigieren.", "Validierung", isError: true);
            return;
        }

        _createCts?.Cancel();
        _createCts = new CancellationTokenSource();
        IsRunning = true;
        _outputBuffer.Clear();
        Output = string.Empty;

        // Fortschritt initialisieren — Marquee bis erste Stage erkannt wird.
        // Die Progress-Sektion sitzt dauerhaft in der Live-Output-Card (rechts);
        // Idle/Run wird über IsCreateProgressIndeterminate + CreateProgressPercent gesteuert.
        CreateProgressPercent = 0;
        CreateProgressText = "Vorbereitung …";
        IsCreateProgressIndeterminate = true;

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

            var progress = new DispatcherProgress<string>(OnPsLine);

            var result = await _containerService.CreateContainerAsync(request, progress, _createCts.Token);
            if (result.WasCancelled)
            {
                CreateProgressText = "Abgebrochen";
                IsCreateProgressIndeterminate = false;
                _dialogService.ShowMessage("Erstellung abgebrochen.", "Abgebrochen");
            }
            else if (result.Success)
            {
                CreateProgressPercent = 100;
                CreateProgressText = "Fertig";
                IsCreateProgressIndeterminate = false;
                _dialogService.ShowMessage(
                    $"Container '{ContainerName}' erstellt.\nDauer: {result.Duration:mm\\:ss}",
                    "Erfolg");

                // Klartext-Passwort nicht länger als nötig im Singleton-VM
                // halten — nach Erfolg leeren (bei Fehler bleibt es für den
                // Retry stehen). Reihenfolge ist bewusst Set → ClearErrors:
                // der Setter re-validiert (NotifyDataErrorInfo) und würde
                // einen vorherigen ClearErrors sofort überschreiben. Beides
                // läuft im selben Dispatcher-Frame — der transiente Fehler
                // wird nie gerendert, das Feld endet garantiert fehlerfrei.
                Password = string.Empty;
                ShowPassword = false;
                ClearErrors(nameof(Password));
            }
            else
            {
                CreateProgressText = "Fehlgeschlagen";
                IsCreateProgressIndeterminate = false;
                _dialogService.ShowMessage(
                    "Erstellung fehlgeschlagen:\n\n" + string.Join("\n", result.Errors),
                    "Fehler",
                    isError: true);
            }
        }
        catch (Exception ex)
        {
            CreateProgressText = "Fehlgeschlagen";
            IsCreateProgressIndeterminate = false;
            _logger.LogError(ex, "Create-Container geworfen");
            _dialogService.ShowMessage(ex.Message, "Fehler", isError: true);
        }
        finally
        {
            IsRunning = false;
            // Der finale Status (Fertig / Fehlgeschlagen / Abgebrochen) wurde vorher
            // gesetzt und bleibt in der Progress-Sektion sichtbar; der Erfolgs-/Fehler-
            // Dialog hat den Run zusätzlich quittiert.
        }
    }

    /// <summary>
    /// Wird für jede stdout/stderr-Zeile aus dem externen powershell.exe
    /// aufgerufen. Hängt sie an einen <see cref="StringBuilder"/> mit
    /// hartem Cap (siehe <see cref="OutputMaxChars"/>) — sonst wächst die
    /// gebundene TextBox bei einem Image-Pull (zigtausende Layer-Zeilen)
    /// in einen WPF-Layout-Stack-Overflow.
    /// </summary>
    private void OnPsLine(string line)
    {
        _outputBuffer.Append(line).Append(Environment.NewLine);

        if (_outputBuffer.Length > OutputMaxChars)
        {
            // Vom Anfang abschneiden, damit das Ende (= aktueller Verlauf)
            // sichtbar bleibt — und zwar an der nächsten Zeilengrenze, damit
            // keine halben Zeilen (z. B. abgeschnittene Fehlermeldungen) im
            // sichtbaren Output stehen.
            var cut = _outputBuffer.Length - OutputMaxChars;
            while (cut < _outputBuffer.Length && _outputBuffer[cut] != '\n')
            {
                cut++;
            }
            if (cut < _outputBuffer.Length)
            {
                cut++; // das '\n' selbst mit entfernen
            }
            _outputBuffer.Remove(0, cut);
            _outputBuffer.Insert(0, "[…früherer Output gekürzt…]" + Environment.NewLine);
        }

        Output = _outputBuffer.ToString();

        var stage = ContainerCreateProgressMapper.Match(line);
        if (stage is null) return;

        // Erste erkannte Stage beendet die Marquee-Phase.
        IsCreateProgressIndeterminate = false;

        if (stage.Percent > CreateProgressPercent)
        {
            CreateProgressPercent = stage.Percent;
            CreateProgressText = stage.Text;
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
        // Beide Commands hängen von IsRunning ab — sonst bleibt der Erstellen-
        // Button am Run-Ende disabled und Abbrechen am Run-Start enabled.
        CreateCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsCreateFormEnabled));
        OnPropertyChanged(nameof(TaskbarProgressState));
    }

    // Passwort-Pflicht hängt vom Auth-Typ ab: beim Wechsel neu bewerten,
    // damit ein Fehler-Rahmen bei Windows-Auth verschwindet (und umgekehrt).
    partial void OnSelectedAuthTypeChanged(AuthType value) =>
        ValidateProperty(Password, nameof(Password));

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
