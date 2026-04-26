using BcContainerCreator.Core.Models;

namespace BcContainerCreator.Core.Docker;

/// <summary>
/// Steuert und befragt die lokale Docker-Installation.
/// </summary>
public interface IDockerService
{
    /// <summary>Prüft, ob Docker (CLI) installiert und im PATH ist.</summary>
    Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default);

    /// <summary>Prüft, ob der Docker-Daemon erreichbar ist (<c>docker info</c> liefert).</summary>
    Task<bool> IsRunningAsync(CancellationToken cancellationToken = default);

    /// <summary>Liest den aktuellen Container-Modus (Windows / Linux).</summary>
    Task<ContainerMode> GetContainerModeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Schaltet Docker Desktop in den Windows-Container-Modus. Erfordert Admin-Rechte
    /// und eine installierte Docker-Desktop-Instanz mit dem CLI-Helper
    /// <c>DockerCli.exe -SwitchDaemon</c>.
    /// </summary>
    Task<bool> SwitchToWindowsModeAsync(CancellationToken cancellationToken = default);
}
