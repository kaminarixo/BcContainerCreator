using System.Security.Principal;

namespace BcContainerLauncher.Core.Setup;

/// <summary>
/// Hilfsklasse, die einmalig prüft, ob der aktuelle Prozess mit
/// Administrator-Rechten läuft. Cached das Ergebnis, weil
/// <see cref="WindowsIdentity.GetCurrent()"/> nicht ganz billig ist.
/// </summary>
public static class AdminContext
{
    private static readonly Lazy<bool> _isAdmin = new(() =>
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    });

    /// <summary>True, wenn der aktuelle Prozess elevated läuft.</summary>
    public static bool IsCurrentProcessAdmin => _isAdmin.Value;
}
