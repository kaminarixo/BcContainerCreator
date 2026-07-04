namespace BcContainerCreator.Core.Models;

/// <summary>
/// Ergebnis eines einzelnen Preflight-Checks. <see cref="FixId"/> identifiziert
/// die zugehörige Fix-Aktion in <c>ISetupService</c>, falls <see cref="IsFixable"/> true ist.
/// </summary>
/// <param name="Name">Anzeigename des Checks.</param>
/// <param name="Status">Ergebnis-Status.</param>
/// <param name="Message">Menschlich lesbare Detail-Meldung.</param>
/// <param name="IsFixable">True, wenn eine automatische Fix-Aktion existiert.</param>
/// <param name="FixId">Schlüssel der Fix-Aktion in <c>ISetupService.AvailableFixes</c>.</param>
/// <param name="RequiresAdminForFix">True, wenn der Fix eine UAC-Elevation auslöst.</param>
/// <param name="HelpUrl">Optionale Doku-/Download-URL für manuelle Behebung.</param>
public sealed record CheckResult(
    string Name,
    CheckStatus Status,
    string Message,
    bool IsFixable = false,
    string? FixId = null,
    bool RequiresAdminForFix = false,
    string? HelpUrl = null);
