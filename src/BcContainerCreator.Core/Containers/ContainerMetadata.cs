using BcContainerCreator.Core.Models;

namespace BcContainerCreator.Core.Containers;

/// <summary>
/// Metadaten zu einem Container, die der Creator beim Erstellen ablegt
/// — damit später im Verwaltungs-Tab das Zugangs-Popup mit Credentials
/// gefüllt werden kann.
///
/// <see cref="PasswordCipher"/> ist DPAPI-CurrentUser-verschlüsselt; damit
/// kann nur der Windows-User, der den Container erstellt hat, das Passwort
/// auf demselben Rechner wieder entschlüsseln.
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
    string WebClientUrl);
