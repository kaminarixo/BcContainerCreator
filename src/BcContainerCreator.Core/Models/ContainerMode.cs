namespace BcContainerCreator.Core.Models;

/// <summary>
/// Aktueller Docker-Container-Modus auf dem Host.
/// </summary>
public enum ContainerMode
{
    /// <summary>Modus nicht ermittelbar (Docker nicht erreichbar oder unerwartete Ausgabe).</summary>
    Unknown,

    /// <summary>Windows-Container-Modus — Voraussetzung für BC-Container.</summary>
    Windows,

    /// <summary>Linux-Container-Modus — muss für BC-Container umgeschaltet werden.</summary>
    Linux
}
