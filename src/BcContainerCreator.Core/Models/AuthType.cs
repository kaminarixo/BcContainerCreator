namespace BcContainerCreator.Core.Models;

/// <summary>
/// Authentifizierungs-Mechanismus für den Container.
/// </summary>
public enum AuthType
{
    /// <summary>Windows-Authentifizierung — der Container übernimmt den Host-User.</summary>
    Windows,

    /// <summary>Benutzername/Passwort via <c>PSCredential</c> (BC-Standard für lokale Dev-Container).</summary>
    NavUserPassword
}
