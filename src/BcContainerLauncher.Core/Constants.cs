namespace BcContainerLauncher.Core;

/// <summary>
/// Globale Konstanten für die BC Container Creator Core-Library.
/// </summary>
public static class Constants
{
    /// <summary>Name des PowerShell-Moduls für BC-Container-Verwaltung.</summary>
    public const string BcContainerHelperModule = "BcContainerHelper";

    /// <summary>Veraltetes Vorgänger-Modul, das ggf. konflitkt-frei entfernt werden muss.</summary>
    public const string LegacyNavContainerHelperModule = "navcontainerhelper";

    /// <summary>Standard-PowerShell-Repository.</summary>
    public const string PSGalleryRepository = "PSGallery";

    /// <summary>Microsoft Endpoint für BC Artifacts.</summary>
    public const string BcArtifactsHost = "bcartifacts.azureedge.net";

    /// <summary>Default-Country für BC-Artifacts.</summary>
    public const string DefaultCountry = "DE";

    /// <summary>Default-Version-Selector.</summary>
    public const string DefaultVersion = "latest";

    /// <summary>Verfügbare Country-Codes (Phase 1: kuratierte Auswahl).</summary>
    public static readonly IReadOnlyList<string> SupportedCountries = new[] { "DE", "W1", "US", "AT", "CH", "GB", "FR", "IT", "ES", "NL", "BE" };
}
