namespace BcContainerCreator.Core.Models;

/// <summary>
/// Status eines einzelnen Preflight-Checks.
/// </summary>
public enum CheckStatus
{
    Ok,
    Warning,
    Failed,
    Pending
}
