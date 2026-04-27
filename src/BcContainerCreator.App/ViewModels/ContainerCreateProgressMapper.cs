namespace BcContainerCreator.App.ViewModels;

/// <summary>
/// Heuristische Stufen-basierte Fortschrittsanzeige. <c>New-BcContainer</c>
/// hat keine echte 0–100%-API; wir scannen die Log-Zeilen auf bekannte
/// Marker und mappen sie auf Prozent-Stufen. Der zurückgegebene Wert ist
/// monoton (Aufrufer darf nur erhöhen, nie senken).
/// </summary>
internal static class ContainerCreateProgressMapper
{
    public sealed record Stage(int Percent, string Text);

    private static readonly (string Pattern, Stage Stage)[] Mapping =
    [
        // Marker → Mindeststufe.
        ("BcContainerHelper version",                       new Stage(10, "BcContainerHelper geladen")),
        ("Artifact-URL:",                                   new Stage(15, "Artifact-URL ermittelt")),
        ("Pulling image",                                   new Stage(40, "Image-Download läuft")),
        ("Downloading",                                     new Stage(40, "Image-Download läuft")),
        ("Extracting",                                      new Stage(40, "Image-Download läuft")),
        ("generic image",                                   new Stage(40, "Image-Vorbereitung läuft")),
        ("Pulling generic",                                 new Stage(40, "Image-Vorbereitung läuft")),
        ("Creating Container",                              new Stage(55, "Container wird erstellt")),
        ("Creating container",                              new Stage(55, "Container wird erstellt")),
        ("New-BcContainer",                                 new Stage(25, "Docker-Vorbereitung")),
        ("Starting Container",                              new Stage(70, "Container wird gestartet")),
        ("Starting container",                              new Stage(70, "Container wird gestartet")),
        ("started",                                         new Stage(70, "Container läuft an")),
        ("Installing",                                      new Stage(85, "BC-Setup läuft")),
        ("Configuring Business Central",                    new Stage(85, "BC-Setup läuft")),
        ("Creating tenant",                                 new Stage(85, "Tenant wird angelegt")),
        ("database",                                        new Stage(85, "Datenbank-Setup")),
        ("Container wurde erstellt",                        new Stage(100, "Fertig")),
        ("erfolgreich",                                     new Stage(100, "Fertig")),
        ("Container ready",                                 new Stage(100, "Fertig")),
        ("Initialization took",                             new Stage(95, "Abschlussprüfung")),
    ];

    /// <summary>
    /// Liefert den höchstmöglichen Stage-Match für eine Zeile, oder null
    /// wenn kein Marker passt.
    /// </summary>
    public static Stage? Match(string line)
    {
        if (string.IsNullOrEmpty(line)) return null;
        Stage? best = null;
        foreach (var (pattern, stage) in Mapping)
        {
            if (line.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                if (best is null || stage.Percent > best.Percent)
                {
                    best = stage;
                }
            }
        }
        return best;
    }
}
