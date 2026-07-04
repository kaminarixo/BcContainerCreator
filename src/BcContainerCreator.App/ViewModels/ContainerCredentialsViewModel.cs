using System.Windows;
using System.Windows.Threading;
using BcContainerCreator.Core.Containers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BcContainerCreator.App.ViewModels;

/// <summary>
/// Zeigt die Zugangs-Info zu einem Container, sofern der Creator ihn erstellt
/// hat (sonst Hinweis "Container wurde nicht mit dem Creator erstellt").
/// Passwort kann ein-/ausgeblendet und in die Zwischenablage kopiert werden.
/// </summary>
public sealed partial class ContainerCredentialsViewModel : ObservableObject
{
    public string ContainerName { get; }
    public bool HasMetadata { get; }
    public string AuthType { get; }
    public string Username { get; }
    public string PasswordPlain { get; }
    public string WebClientUrl { get; }
    public string ArtifactType { get; }
    public string Country { get; }
    public string VersionSelector { get; }
    public string CreatedAtDisplay { get; }
    public string MissingMessage { get; }

    /// <summary>Windows-Konto, das den Container erstellt hat (leer bei Alt-Metadaten).</summary>
    public string CreatedBy { get; }

    public bool HasPassword => !string.IsNullOrEmpty(PasswordPlain);

    /// <summary>
    /// True, wenn ein verschlüsseltes Passwort existiert, aber unter dem
    /// aktuellen Windows-Konto nicht entschlüsselbar ist (DPAPI-CurrentUser-
    /// Bindung an das Erstell-Konto).
    /// </summary>
    public bool IsPasswordLocked { get; }

    /// <summary>Erklärender Hinweis, wenn <see cref="IsPasswordLocked"/> true ist.</summary>
    public string PasswordLockedMessage { get; }

    private DispatcherTimer? _clipboardClearTimer;
    private string? _lastCopiedPassword;

    /// <summary>Statushinweis unter dem Passwort-Feld (Copy/Auto-Clear).</summary>
    [ObservableProperty]
    private string _clipboardHint = string.Empty;

    [ObservableProperty]
    private bool _showPassword;

    public string PasswordDisplay => ShowPassword || string.IsNullOrEmpty(PasswordPlain)
        ? PasswordPlain
        : new string('•', Math.Min(PasswordPlain.Length, 12));

    partial void OnShowPasswordChanged(bool value) => OnPropertyChanged(nameof(PasswordDisplay));

    public ContainerCredentialsViewModel(string containerName, ContainerMetadata? metadata, string? decryptedPassword)
    {
        ContainerName = containerName;
        HasMetadata = metadata is not null;

        if (metadata is null)
        {
            AuthType = string.Empty;
            Username = string.Empty;
            PasswordPlain = string.Empty;
            WebClientUrl = $"http://{containerName}/BC?tenant=default";
            ArtifactType = string.Empty;
            Country = string.Empty;
            VersionSelector = string.Empty;
            CreatedAtDisplay = string.Empty;
            CreatedBy = string.Empty;
            IsPasswordLocked = false;
            PasswordLockedMessage = string.Empty;
            MissingMessage = "Dieser Container wurde nicht mit dem BC Container Creator erstellt — daher sind keine gespeicherten Zugangsdaten verfügbar. Web-Client-URL ist trotzdem verlinkt.";
            return;
        }

        AuthType = metadata.AuthType.ToString();
        Username = metadata.Username;
        PasswordPlain = decryptedPassword ?? string.Empty;
        WebClientUrl = metadata.WebClientUrl;
        ArtifactType = metadata.ArtifactType.ToString();
        Country = metadata.Country;
        VersionSelector = string.IsNullOrEmpty(metadata.ResolvedBuild)
            ? metadata.VersionSelector
            : $"{metadata.VersionSelector}  ({metadata.ResolvedBuild})";
        CreatedAtDisplay = metadata.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        CreatedBy = metadata.CreatedBy ?? string.Empty;
        MissingMessage = string.Empty;

        // Cipher vorhanden, aber Decrypt lieferte nichts → DPAPI-Konto-Mismatch.
        // Statt das Passwort kommentarlos wegzulassen, dem User erklären warum.
        IsPasswordLocked = metadata.PasswordCipher is { Length: > 0 } && string.IsNullOrEmpty(decryptedPassword);
        PasswordLockedMessage = !IsPasswordLocked
            ? string.Empty
            : string.IsNullOrEmpty(CreatedBy)
                ? "Das gespeicherte Passwort ist an das Windows-Konto gebunden, das den Container erstellt hat, und kann unter dem aktuellen Konto nicht entschlüsselt werden."
                : $"Das gespeicherte Passwort ist an das Windows-Konto „{CreatedBy}“ gebunden und kann unter dem aktuellen Konto nicht entschlüsselt werden.";
    }

    [RelayCommand]
    private void CopyUrl() => CopyToClipboard(WebClientUrl);

    [RelayCommand]
    private void CopyUsername() => CopyToClipboard(Username);

    [RelayCommand]
    private void CopyPassword()
    {
        if (string.IsNullOrEmpty(PasswordPlain)) return;
        CopyToClipboard(PasswordPlain);

        // Zwischenablage nach 30 s automatisch leeren — aber nur, wenn sie
        // dann noch das Passwort enthält (fremde Inhalte nie überschreiben).
        // Der Timer überlebt bewusst ein Schließen des Fensters: das Clipboard
        // soll auch dann geleert werden. Er feuert genau einmal, stoppt sich
        // selbst und gibt damit das VM frei — kein unbegrenzter Leak.
        _lastCopiedPassword = PasswordPlain;
        ClipboardHint = "Passwort kopiert — Zwischenablage wird in 30 s geleert.";
        _clipboardClearTimer?.Stop();
        _clipboardClearTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _clipboardClearTimer.Tick += (_, _) => ClearClipboardIfStillPassword();
        _clipboardClearTimer.Start();
    }

    private void ClearClipboardIfStillPassword()
    {
        _clipboardClearTimer?.Stop();
        _clipboardClearTimer = null;
        try
        {
            if (_lastCopiedPassword is not null
                && Clipboard.ContainsText()
                && Clipboard.GetText() == _lastCopiedPassword)
            {
                Clipboard.Clear();
                ClipboardHint = "Zwischenablage geleert.";
            }
            else
            {
                ClipboardHint = string.Empty;
            }
        }
        catch
        {
            // Clipboard kann von einem anderen Prozess gesperrt sein — best effort.
        }
        _lastCopiedPassword = null;
    }

    [RelayCommand]
    private void OpenUrl()
    {
        if (string.IsNullOrEmpty(WebClientUrl)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(WebClientUrl) { UseShellExecute = true });
        }
        catch { /* still */ }
    }

    private static void CopyToClipboard(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try { Clipboard.SetText(text); } catch { /* still */ }
    }
}
