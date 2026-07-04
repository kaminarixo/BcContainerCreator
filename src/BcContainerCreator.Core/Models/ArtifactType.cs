namespace BcContainerCreator.Core.Models;

/// <summary>
/// Typ des BC-Artifacts. <see cref="OnPrem"/> für klassische BC-On-Premise-Builds,
/// <see cref="Sandbox"/> für die Cloud-/Sandbox-Variante.
/// </summary>
public enum ArtifactType
{
    /// <summary>Klassisches On-Premise-Artifact (<c>-type OnPrem</c>).</summary>
    OnPrem,

    /// <summary>Cloud-/Sandbox-Artifact (<c>-type Sandbox</c>).</summary>
    Sandbox
}
