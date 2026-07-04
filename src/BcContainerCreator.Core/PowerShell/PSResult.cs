namespace BcContainerCreator.Core.PowerShell;

/// <summary>
/// Konsolidiertes Ergebnis einer PowerShell-Ausführung (extern via powershell.exe).
/// </summary>
/// <param name="Success">Wahr, wenn der powershell.exe-Prozess Exit-Code 0 hatte und nicht abgebrochen wurde.</param>
/// <param name="Objects">stdout-Zeilen als <see cref="string"/>. Bewusst kein <c>PSObject</c>: der externe Runner liefert reine Text-Zeilen, und das alte Wrapping zog die schwere <c>Microsoft.PowerShell.SDK</c>-Dependency unnötig in die Core-Library.</param>
/// <param name="Errors">stderr-Zeilen + Fehler aus dem Wrapper-Catch.</param>
/// <param name="Duration">Gesamtlaufzeit.</param>
/// <param name="WasCancelled">Per <see cref="System.Threading.CancellationToken"/> abgebrochen.</param>
/// <param name="ExitCode">Roh-Exit-Code des powershell.exe-Prozesses. Nur aussagekräftig,
/// wenn <paramref name="Success"/> false ist: Fehlerpfade setzen -1 (Process-Start-Fehler,
/// Abbruch vor Start), das der Default 0 nicht abbildet.</param>
public sealed record PSResult(
    bool Success,
    IReadOnlyList<string> Objects,
    IReadOnlyList<string> Errors,
    TimeSpan Duration,
    bool WasCancelled = false,
    int ExitCode = 0);
