using BcContainerLauncher.Core.Models;

namespace BcContainerLauncher.Core.Setup;

/// <summary>
/// Führt alle Voraussetzungs-Checks für die BC-Container-Erstellung aus.
/// </summary>
public interface IPreflightCheck
{
    /// <summary>
    /// Führt alle Checks aus und liefert das Ergebnis pro Check.
    /// Reihenfolge ist deterministisch (siehe <see cref="GetCheckIds"/>).
    /// </summary>
    Task<IReadOnlyList<CheckResult>> RunAllAsync(IProgress<CheckResult>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Liefert die geordneten IDs aller Checks (für UI-Vorbefüllung).</summary>
    IReadOnlyList<string> GetCheckIds();
}
