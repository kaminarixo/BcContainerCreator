using System.Security;

namespace BcContainerCreator.Core.Models;

/// <summary>
/// Vollständige Anfrage zum Erstellen eines BC-Containers. Wird vom UI/CLI
/// gefüllt und von <c>IContainerService.CreateContainerAsync</c> in einen
/// PowerShell-Aufruf übersetzt.
/// </summary>
/// <param name="ContainerName">Eindeutiger Container-Name (Docker).</param>
/// <param name="ArtifactType">OnPrem oder Sandbox.</param>
/// <param name="Country">Länder-Code (z. B. "DE").</param>
/// <param name="Version">Version-Selector ("latest", "26", "26.0.1234.5678").</param>
/// <param name="AuthType">Windows oder NavUserPassword.</param>
/// <param name="Username">Benutzername für NavUserPassword.</param>
/// <param name="Password">Passwort als <see cref="SecureString"/>; nie als String.</param>
/// <param name="LicenseFilePath">Optionaler Pfad zur .flf-Lizenz.</param>
/// <param name="AcceptEula">EULA-Bestätigung (default true).</param>
/// <param name="IncludeAL">AL-Tooling für VS Code mounten.</param>
/// <param name="IncludeTestToolkit">TestToolkit installieren (Phase 1: false).</param>
/// <param name="MemoryLimit">Optionales RAM-Limit (z. B. "8G"). Null = kein Limit.</param>
/// <param name="Isolation">Isolation-Mode: "process", "hyperv" oder null (Default).</param>
/// <param name="Multitenant">Multitenant-Container (mehrere BC-Tenants in einem Container).</param>
public sealed record ContainerCreateRequest(
    string ContainerName,
    ArtifactType ArtifactType,
    string Country,
    string Version,
    AuthType AuthType,
    string Username,
    SecureString Password,
    string? LicenseFilePath = null,
    bool AcceptEula = true,
    bool IncludeAL = true,
    bool IncludeTestToolkit = false,
    string? MemoryLimit = null,
    string? Isolation = null,
    bool Multitenant = false);
