namespace BcContainerCreator.Core.PowerShell;

/// <summary>
/// Event-Args für gestreamte PowerShell-Ausgabe. <see cref="Timestamp"/> wird
/// in UTC gesetzt.
/// </summary>
public sealed class PowerShellOutputEventArgs : EventArgs
{
    /// <summary>Erzeugt Event-Args für eine Output-Zeile; Timestamp = jetzt (UTC).</summary>
    public PowerShellOutputEventArgs(PSStreamType type, string message)
    {
        Type = type;
        Message = message;
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <summary>Stream, aus dem die Zeile stammt (stdout → Information, stderr → Error).</summary>
    public PSStreamType Type { get; }

    /// <summary>Die Roh-Textzeile.</summary>
    public string Message { get; }

    /// <summary>Empfangszeitpunkt in UTC.</summary>
    public DateTimeOffset Timestamp { get; }
}
