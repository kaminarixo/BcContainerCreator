using System.IO;

namespace BcContainerCreator.Core.PowerShell;

/// <summary>
/// Zentraler Ablageort für kurzlebige Laufzeit-Dateien (generierte Skripte,
/// Parameter-JSON): <c>%LOCALAPPDATA%\BcContainerCreator\runtime</c>.
/// <para>
/// Die Default-ACL von <c>%LOCALAPPDATA%</c> beschränkt den Zugriff auf den
/// aktuellen User plus SYSTEM und Administrators — ohne dass pro Datei eine
/// ACL gesetzt werden muss (kein Create-dann-ACL-Race wie im globalen Temp).
/// Der Administrators-Zugriff ist gewollt: elevated gestartete Prozesse
/// (UAC-Prompt, ggf. anderes Admin-Konto) müssen hier abgelegte Skripte per
/// <c>-File</c> lesen können.
/// </para>
/// </summary>
public static class RuntimePaths
{
    /// <summary>
    /// Liefert das Runtime-Verzeichnis und stellt sicher, dass es existiert.
    /// </summary>
    public static string GetRuntimeDirectory()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BcContainerCreator", "runtime");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
