using BcContainerLauncher.Core.Models;
using BcContainerLauncher.Core.PowerShell;

namespace BcContainerLauncher.Core.Containers;

/// <summary>
/// Container-Operationen (Phase 1: nur Erstellung).
/// </summary>
public interface IContainerService
{
    /// <summary>
    /// Erstellt einen BC-Container gemäß <paramref name="request"/>.
    /// Live-Output wird zusätzlich über <see cref="IPowerShellRunner.OutputReceived"/>
    /// gestreamt; <paramref name="progress"/> liefert einen kompakten Statustext.
    /// </summary>
    Task<PSResult> CreateContainerAsync(
        ContainerCreateRequest request,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Baut das PowerShell-Skript, das <see cref="CreateContainerAsync"/> ausführen würde.
    /// Public, damit es testbar und im UI als Vorschau anzeigbar ist.
    /// </summary>
    string BuildCreateScript(ContainerCreateRequest request);
}
