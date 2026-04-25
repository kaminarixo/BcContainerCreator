using System.Management.Automation;

namespace BcContainerLauncher.Core.PowerShell;

/// <summary>
/// Konsolidiertes Ergebnis einer PowerShell-Ausführung.
/// </summary>
/// <param name="Success">Wahr, wenn keine Errors aufgetreten sind und das Skript komplett lief.</param>
/// <param name="Objects">Pipeline-Output-Objekte.</param>
/// <param name="Errors">Aggregierte Fehlermeldungen (auch ParseErrors / NonTerminating).</param>
/// <param name="Duration">Gesamtlaufzeit.</param>
/// <param name="WasCancelled">Wahr, wenn die Ausführung per CancellationToken abgebrochen wurde.</param>
public sealed record PSResult(
    bool Success,
    IReadOnlyList<PSObject> Objects,
    IReadOnlyList<string> Errors,
    TimeSpan Duration,
    bool WasCancelled = false);
