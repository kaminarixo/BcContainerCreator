namespace BcContainerCreator.Core.PowerShell;

/// <summary>
/// Typ eines PowerShell-Output-Streams.
/// </summary>
public enum PSStreamType
{
    Information,
    Warning,
    Error,
    Verbose,
    Debug,
    Progress
}
