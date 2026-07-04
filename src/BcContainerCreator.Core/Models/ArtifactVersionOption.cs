namespace BcContainerCreator.Core.Models;

/// <summary>
/// Eine wählbare Versions-Option für Container-Create.
/// <see cref="Selector"/> ist der Wert, der an <c>Get-BcArtifactUrl -version</c>
/// übergeben wird ("latest", "28", "27", ...). <see cref="LatestBuild"/> ist
/// die konkrete Build-Version, die "Selector" aktuell auflöst (für die UI-Anzeige).
/// </summary>
public sealed record ArtifactVersionOption(
    string Selector,
    string? LatestBuild)
{
    /// <summary>Anzeige-Text für die UI: Selector, ergänzt um den aufgelösten Build.</summary>
    public string Display => string.IsNullOrEmpty(LatestBuild)
        ? Selector
        : $"{Selector}  —  {LatestBuild}";
}
