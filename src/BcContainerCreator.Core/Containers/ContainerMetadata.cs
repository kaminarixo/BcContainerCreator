using BcContainerCreator.Core.Models;

namespace BcContainerCreator.Core.Containers;

/// <summary>
/// Metadaten zu einem Container, die der Creator beim Erstellen ablegt
/// — damit später im Verwaltungs-Tab das Zugangs-Popup mit Credentials
/// gefüllt werden kann.
///
/// <see cref="PasswordCipher"/> ist DPAPI-CurrentUser-verschlüsselt; damit
/// kann nur der Windows-User, der den Container erstellt hat, das Passwort
/// auf demselben Rechner wieder entschlüsseln. <see cref="CreatedBy"/> hält
/// dieses Konto fest (DOMAIN\User bzw. RECHNER\User), damit die UI bei einem
/// Konto-Mismatch erklären kann, warum das Passwort nicht anzeigbar ist.
/// Null bei Dateien aus älteren App-Versionen.
/// </summary>
public sealed record ContainerMetadata(
    string Name,
    DateTimeOffset CreatedAt,
    AuthType AuthType,
    string Username,
    byte[]? PasswordCipher,
    ArtifactType ArtifactType,
    string Country,
    string VersionSelector,
    string? ResolvedBuild,
    string WebClientUrl,
    string? CreatedBy = null);
