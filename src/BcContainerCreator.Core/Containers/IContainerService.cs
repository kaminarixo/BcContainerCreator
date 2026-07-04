using BcContainerCreator.Core.Models;
using BcContainerCreator.Core.PowerShell;

namespace BcContainerCreator.Core.Containers;

/// <summary>
/// Container-Operationen: Erstellung, Versions-Auflistung und Verwaltung
/// (Liste, Start, Stop, Entfernen, Logs).
/// </summary>
public interface IContainerService
{
    /// <summary>
    /// Erstellt einen BC-Container via <c>New-BcContainer</c>. Streamt den
    /// PowerShell-Output zeilenweise über <paramref name="progress"/> und
    /// persistiert bei Erfolg die Container-Metadaten (inkl. DPAPI-verschlüsseltem
    /// Passwort) im Metadata-Store.
    /// </summary>
    Task<PSResult> CreateContainerAsync(
        ContainerCreateRequest request,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Baut das <c>New-BcContainer</c>-Skript für einen Request — ohne es
    /// auszuführen. Wirft <see cref="ArgumentException"/> bei ungültigen
    /// Eingaben (Containername, Country, Version).
    /// </summary>
    string BuildCreateScript(ContainerCreateRequest request);

    /// <summary>
    /// Liefert eine Liste auswählbarer Major-Versionen (BC LTS-Releases).
    /// Erster Eintrag ist immer "latest"; danach die jüngsten N Major-Versionen
    /// (28, 27, 26, …) jeweils mit der konkret aufgelösten Build-Version.
    /// </summary>
    Task<IReadOnlyList<ArtifactVersionOption>> GetVersionOptionsAsync(
        ArtifactType type,
        string country,
        int topMajors = 6,
        CancellationToken cancellationToken = default);

    /// <summary>Listet alle vorhandenen Container (laufend + gestoppt).</summary>
    Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(CancellationToken cancellationToken = default);

    /// <summary>Startet einen Container per Name.</summary>
    Task<bool> StartContainerAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Stoppt einen Container per Name (sanft).</summary>
    Task<bool> StopContainerAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Entfernt einen Container per Name. <paramref name="force"/> stoppt vorher.</summary>
    Task<bool> RemoveContainerAsync(string name, bool force = true, CancellationToken cancellationToken = default);

    /// <summary>Holt die letzten <paramref name="tail"/> Log-Zeilen eines Containers (stdout+stderr).</summary>
    Task<string> GetContainerLogsAsync(string name, int tail = 1000, CancellationToken cancellationToken = default);
}
