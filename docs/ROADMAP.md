# Roadmap

> Diese Roadmap ist unverbindlich. Umfang, Reihenfolge und Prioritäten können sich jederzeit ändern.

## Current Status

Aktueller Funktionsumfang der App:

- Windows-Desktop-App für Business-Central-Container
- Diagnose der Voraussetzungen (Docker, Windows-Edition, PowerShell, BcContainerHelper, …)
- Container-Erstellung über `BcContainerHelper`
- Externe Windows PowerShell 5.1 statt In-Process-Runspace
- Container-Verwaltung mit Start, Stop, Löschen, Logs und „Web Client öffnen"
- Live-Output und File-Logging
- DPAPI-verschlüsselte Container-Zugangsdaten
- Setup.exe über GitHub Releases

## Next Improvements

Geplante kleinere Verbesserungen:

- Kleinere UX-Verbesserungen im Container-Erstellen-Tab
- Verständlichere Fehlermeldungen bei Docker-, Artifact-, Lizenz- und PowerShell-Problemen
- Verbesserte Diagnose-Ausgabe mit Copy- und Export-Funktion
- Prüfung auf neue `BcContainerHelper`-Versionen
- Port-Konflikte erkennen und verständlich melden
- Anzeige von Artifact- und Docker-Cache-Informationen

## Profiles

Geplant: Container-Konfigurationen als wiederverwendbare Profile.

- Container-Konfigurationen als Profile speichern
- Profile laden und wiederverwenden
- Profile duplizieren oder löschen
- Profile als JSON exportieren und importieren
- Standardprofil für häufig genutzte Setups festlegen
- Sensible Werte wie Passwörter werden nicht ungeschützt exportiert

## Health Check

Geplant: zusätzliche Statusinformationen pro Container.

- Container-Health-Status anzeigen
- Prüfen, ob Docker läuft
- Prüfen, ob ein Container läuft
- Prüfen, ob der BC Web Client erreichbar ist
- Prüfen, ob wichtige Ports erreichbar sind
- Prüfen, ob Container-Logs kritische Fehler enthalten
- Statusampel oder Badge pro Container
- Diagnosebericht für einen einzelnen Container erzeugen

## Updates and Distribution

Geplante und mögliche Verteilungsoptionen:

- Auto-Update-Prüfung über GitHub Releases
- Hinweis in der App, sobald eine neue Version verfügbar ist
- Optionaler Download-Link zur neuesten Setup.exe
- Portable ZIP-Version ohne Installer
- MSI-Installer als spätere Option
- Release Notes direkt in der App anzeigen

## Container Management

Geplante Erweiterungen rund um die Container-Verwaltung:

- Restart-Aktion ergänzen
- Container klonen oder aus bestehendem Profil neu erstellen
- Backup und Restore von Container-Datenbanken
- Auto-Cleanup alter oder gestoppter Container
- Bulk-Aktionen für mehrere Container
- Bessere Log-Ansicht mit Suche und Filter

## Later Developer Workflow Ideas

Mögliche spätere Erweiterungen für Business-Central-Entwickler. Diese Punkte sind ausdrücklich keine Kernfunktionen des Tools, sondern Ideen, die ergänzt werden könnten:

- VS Code Workspace automatisch erzeugen
- AL `launch.json` für einen Container generieren
- Symbols automatisch herunterladen
- `.app`-Dateien aus der GUI publishen, upgraden oder entfernen
- TestToolkit und PerformanceToolkit komfortabler aktivieren
- Container mit einem bestehenden AL-Projektordner verknüpfen

## Later Ideas

Größere Ideen, die langfristig denkbar sind:

- CLI-Modus zusätzlich zur GUI
- Team-Profile über geteilte JSON-Dateien
- Optionaler Build-Server- oder Agent-Modus
- Erweiterte Templates für Schulungen, Demos und Testumgebungen
- GitHub-Issue-Reporter mit automatisch angehängten Diagnoseinformationen
