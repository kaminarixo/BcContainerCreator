namespace BcContainerLauncher.Core.Models;

/// <summary>
/// Ergebnis eines einzelnen Preflight-Checks. <see cref="FixId"/> identifiziert
/// die zugehörige Fix-Aktion in <c>ISetupService</c>, falls <see cref="IsFixable"/> true ist.
/// </summary>
public sealed record CheckResult(
    string Name,
    CheckStatus Status,
    string Message,
    bool IsFixable = false,
    string? FixId = null,
    string? Details = null);
