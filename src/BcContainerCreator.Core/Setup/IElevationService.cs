namespace BcContainerCreator.Core.Setup;

/// <summary>
/// Führt Operationen aus, die zwingend Admin-Rechte brauchen, indem ein
/// Hilfs-Process mit <c>Verb="runas"</c> gestartet wird. Windows zeigt dann
/// den UAC-Prompt — bei nicht-admin-Sessions verlangt er Credentials, die
/// der User mit dem lokalen Admin-Account (z. B. <c>.\admin</c>) eingeben kann.
/// </summary>
public interface IElevationService
{
    /// <summary>
    /// Startet einen elevated Process. Liefert <c>true</c>, wenn der Process
    /// mit ExitCode 0 zurückkam, sonst <c>false</c>. Bricht der User den
    /// UAC-Prompt ab oder schlägt die Authentifizierung fehl, wird <c>false</c>
    /// zurückgegeben (kein Throw).
    /// </summary>
    Task<bool> RunElevatedAsync(
        string fileName,
        string arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}
