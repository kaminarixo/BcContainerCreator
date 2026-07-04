namespace BcContainerCreator.Core.PowerShell;

/// <summary>
/// Typ eines PowerShell-Output-Streams.
/// </summary>
public enum PSStreamType
{
    /// <summary>Information-Stream (stdout, <c>Write-Information</c>/<c>Write-Host</c>).</summary>
    Information,

    /// <summary>Warning-Stream (<c>Write-Warning</c>).</summary>
    Warning,

    /// <summary>Error-Stream (stderr).</summary>
    Error,

    /// <summary>Verbose-Stream (<c>Write-Verbose</c>).</summary>
    Verbose,

    /// <summary>Debug-Stream (<c>Write-Debug</c>).</summary>
    Debug,

    /// <summary>Progress-Stream (<c>Write-Progress</c>).</summary>
    Progress
}
