namespace BcContainerCreator.Core.PowerShell;

/// <summary>
/// Abstraktion für die PowerShell-Ausführung. Hält intern einen persistenten
/// Runspace, damit das BcContainerHelper-Modul nicht pro Aufruf erneut geladen wird.
/// </summary>
public interface IPowerShellRunner : IAsyncDisposable
{
    /// <summary>
    /// Wird für jede gestreamte Zeile aller Streams gefeuert. Der Handler kann
    /// im UI-Thread oder im Background laufen — die Implementierung garantiert
    /// keine Synchronisation.
    /// </summary>
    event EventHandler<PowerShellOutputEventArgs>? OutputReceived;

    /// <summary>
    /// Initialisiert den Runspace (idempotent). Muss vor dem ersten
    /// <see cref="ExecuteAsync"/>-Aufruf aufgerufen werden, sonst geschieht es lazy.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Führt ein PowerShell-Skript im persistenten Runspace aus.
    /// </summary>
    /// <param name="script">PowerShell-Skript (Multiline OK).</param>
    /// <param name="variables">Optionale Variablen, die vor der Ausführung im Runspace gesetzt werden.</param>
    /// <param name="cancellationToken">Bricht das Skript via <c>PowerShell.BeginStop</c> ab.</param>
    Task<PSResult> ExecuteAsync(
        string script,
        IDictionary<string, object?>? variables = null,
        CancellationToken cancellationToken = default);
}
