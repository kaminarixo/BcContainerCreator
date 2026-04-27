using System.Management.Automation;

namespace BcContainerCreator.Core.PowerShell;

/// <summary>
/// Konsolidiertes Ergebnis einer PowerShell-Ausführung (extern via powershell.exe).
/// </summary>
/// <param name="Success">Wahr, wenn der powershell.exe-Prozess Exit-Code 0 hatte und nicht abgebrochen wurde.</param>
/// <param name="Objects">stdout-Zeilen, jeweils als <see cref="PSObject"/>-Wrapper (Backwards-Compat mit bestehenden Aufrufern).</param>
/// <param name="Errors">stderr-Zeilen + Fehler aus dem Wrapper-Catch.</param>
/// <param name="Duration">Gesamtlaufzeit.</param>
/// <param name="WasCancelled">Per <see cref="System.Threading.CancellationToken"/> abgebrochen.</param>
/// <param name="ExitCode">Roh-Exit-Code des powershell.exe-Prozesses (oder -1 bei Process-Start-Fehler).</param>
public sealed record PSResult(
    bool Success,
    IReadOnlyList<PSObject> Objects,
    IReadOnlyList<string> Errors,
    TimeSpan Duration,
    bool WasCancelled = false,
    int ExitCode = 0);
