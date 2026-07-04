namespace BcContainerCreator.Core.Models;

/// <summary>
/// Status eines einzelnen Preflight-Checks.
/// </summary>
public enum CheckStatus
{
    /// <summary>Voraussetzung erfüllt.</summary>
    Ok,

    /// <summary>Erfüllt, aber mit Einschränkung (z. B. nicht empfohlene Konfiguration).</summary>
    Warning,

    /// <summary>Voraussetzung nicht erfüllt — Container-Erstellung wird scheitern.</summary>
    Failed,

    /// <summary>Check wurde noch nicht ausgeführt.</summary>
    Pending
}
