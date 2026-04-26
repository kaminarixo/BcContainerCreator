namespace BcContainerCreator.Core.PowerShell;

/// <summary>
/// Event-Args für gestreamte PowerShell-Ausgabe. <see cref="Timestamp"/> wird
/// in UTC gesetzt.
/// </summary>
public sealed class PowerShellOutputEventArgs : EventArgs
{
    public PowerShellOutputEventArgs(PSStreamType type, string message)
    {
        Type = type;
        Message = message;
        Timestamp = DateTimeOffset.UtcNow;
    }

    public PSStreamType Type { get; }
    public string Message { get; }
    public DateTimeOffset Timestamp { get; }
}
