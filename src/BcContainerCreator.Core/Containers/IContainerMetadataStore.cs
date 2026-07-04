using System.Security;

namespace BcContainerCreator.Core.Containers;

/// <summary>
/// Persistenter Speicher für Container-Metadaten (Username/Passwort,
/// Erstellungs-Parameter). Pro Container eine Datei im User-Profile.
/// </summary>
public interface IContainerMetadataStore
{
    /// <summary>
    /// Speichert Metadaten zu einem Container. Passwort wird DPAPI-verschlüsselt.
    /// <paramref name="createdBy"/> ist das Windows-Konto (DOMAIN\User), an das
    /// die DPAPI-Verschlüsselung faktisch gebunden ist.
    /// </summary>
    Task SaveAsync(
        string containerName,
        DateTimeOffset createdAt,
        Models.AuthType authType,
        string username,
        SecureString? password,
        Models.ArtifactType artifactType,
        string country,
        string versionSelector,
        string? resolvedBuild,
        string webClientUrl,
        string? createdBy = null,
        CancellationToken cancellationToken = default);

    /// <summary>Liest die Metadaten zu einem Container, oder null wenn nicht vorhanden.</summary>
    Task<ContainerMetadata?> LoadAsync(string containerName, CancellationToken cancellationToken = default);

    /// <summary>Entschlüsselt das Passwort aus dem Cipher (DPAPI-CurrentUser).</summary>
    string? DecryptPassword(byte[]? cipher);

    /// <summary>Löscht die Metadaten zu einem Container (z. B. beim Container-Remove).</summary>
    Task DeleteAsync(string containerName, CancellationToken cancellationToken = default);
}
