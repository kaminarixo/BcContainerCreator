namespace BcContainerLauncher.Core.Models;

/// <summary>
/// Typ des BC-Artifacts. <see cref="OnPrem"/> für klassische BC-On-Premise-Builds,
/// <see cref="Sandbox"/> für die Cloud-/Sandbox-Variante.
/// </summary>
public enum ArtifactType
{
    OnPrem,
    Sandbox
}
