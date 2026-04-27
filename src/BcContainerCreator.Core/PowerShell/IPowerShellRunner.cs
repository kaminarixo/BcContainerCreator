namespace BcContainerCreator.Core.PowerShell;

/// <summary>
/// Abstraktion für die PowerShell-Ausführung. Aufrufe werden seriell
/// gegen einen extern gestarteten <c>powershell.exe</c>-Prozess (Windows
/// PowerShell 5.1) ausgeführt — kein In-Process-Runspace, weil der unter
/// BcContainerHelper an <c>AddTypeCommand</c>-Type-Initialisierung
/// scheitert. Variablen wandern als ACL-geschützte JSON-Datei in den
/// User-Temp und werden im Skript via <c>$Params</c> gelesen.
/// </summary>
public interface IPowerShellRunner : IAsyncDisposable
{
    /// <summary>
    /// Wird für jede stdout/stderr-Zeile gefeuert. Da Aufrufe seriell sind,
    /// vermischen sich Zeilen unterschiedlicher Ausführungen nicht.
    /// </summary>
    event EventHandler<PowerShellOutputEventArgs>? OutputReceived;

    /// <summary>
    /// Heute ein No-Op (extern gestartete <c>powershell.exe</c> braucht keinen
    /// Init), bleibt aber als Erweiterungspunkt erhalten — und um die alte
    /// API-Form vorerst kompatibel zu halten.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Führt ein PowerShell-Skript in einer neuen <c>powershell.exe</c>-Instanz aus.
    /// </summary>
    /// <param name="script">User-Skript (wird in einen Try/Catch-Wrapper eingebettet).</param>
    /// <param name="variables">
    /// Wird als JSON-Datei in den User-Temp geschrieben und im Skript
    /// als <c>$Params</c> verfügbar. <see cref="System.Security.SecureString"/>
    /// wird in Plain-Text konvertiert; die Datei wird ACL-restriktiv auf den
    /// aktuellen User gesetzt und nach Run sofort gelöscht.
    /// </param>
    /// <param name="cancellationToken">Killt den powershell.exe-Process inkl. ProcessTree.</param>
    Task<PSResult> ExecuteAsync(
        string script,
        IDictionary<string, object?>? variables = null,
        CancellationToken cancellationToken = default);
}
