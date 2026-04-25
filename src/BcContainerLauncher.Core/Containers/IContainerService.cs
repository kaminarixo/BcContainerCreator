using BcContainerLauncher.Core.Models;
using BcContainerLauncher.Core.PowerShell;

namespace BcContainerLauncher.Core.Containers;

/// <summary>
/// Container-Operationen (Phase 1: nur Erstellung + Versions-Auflistung).
/// </summary>
public interface IContainerService
{
    Task<PSResult> CreateContainerAsync(
        ContainerCreateRequest request,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    string BuildCreateScript(ContainerCreateRequest request);

    /// <summary>
    /// Holt die zuletzt verfügbaren Artifact-Versionen für Type+Country.
    /// Sortiert absteigend, eindeutig, max <paramref name="top"/> Einträge.
    /// </summary>
    Task<IReadOnlyList<string>> GetAvailableVersionsAsync(
        ArtifactType type,
        string country,
        int top = 15,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Löst "latest" zur konkreten Versionsnummer auf (z. B. "26.0.1234.5678").
    /// Liefert null, wenn keine URL gefunden wird.
    /// </summary>
    Task<string?> ResolveLatestVersionAsync(
        ArtifactType type,
        string country,
        CancellationToken cancellationToken = default);
}
