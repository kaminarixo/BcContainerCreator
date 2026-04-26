namespace BcContainerCreator.Core.Models;

/// <summary>
/// Snapshot eines Docker-Containers. Wird über <c>docker ps -a</c> ermittelt
/// und in der Container-Verwaltung angezeigt.
/// </summary>
/// <param name="Id">Volle Container-ID.</param>
/// <param name="Name">Container-Name (eindeutig).</param>
/// <param name="Image">Image-Name inkl. Tag.</param>
/// <param name="Status">Roh-Status-String von Docker (z. B. "Up 2 hours", "Exited (0) 5 minutes ago").</param>
/// <param name="IsRunning">True, wenn der Container aktuell läuft.</param>
/// <param name="Ports">Port-Mappings als Roh-String.</param>
/// <param name="WebClientUrl">Vermutete Web-Client-URL ("http://&lt;name&gt;/BC"), kann null sein wenn nicht ermittelbar.</param>
/// <param name="IsBcContainer">Heuristik: scheint ein BC-Container zu sein (Image enthält "bcartifacts" / "businesscentral").</param>
public sealed record ContainerInfo(
    string Id,
    string Name,
    string Image,
    string Status,
    bool IsRunning,
    string Ports,
    string? WebClientUrl,
    bool IsBcContainer);
