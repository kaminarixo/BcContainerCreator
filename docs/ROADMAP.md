# Roadmap

## Phase 1 — PoC mit tragfähiger Architektur (in Arbeit)

Solution-Struktur, Core-Library, WPF-Shell mit Diagnose-, Container-Create- und Log-Tab. Funktionierende Container-Erstellung mit BcContainerHelper über externen `powershell.exe`-Subprozess, serialisierte Aufrufe, Cancellation, Streaming-Output, 35 xUnit-Tests.

## Phase 2 — Container-Verwaltung

- Liste aller BC-Container mit Status (Running / Exited / Created)
- Start / Stop / Restart / Remove
- "Web Client öffnen"-Button (Default-Browser auf `http://<container>/BC`)
- Container-Logs anzeigen (Streaming aus `docker logs`)
- "VS Code mit Container-Config öffnen" (AL-Workspace generieren / öffnen)

## Phase 3 — Profile & UX

- Speicherbare Konfigurations-Profile (lokal, optional verschlüsselt)
- Export/Import Profile als JSON für Sharing zwischen Kollegen
- Settings-Tab funktional (Defaults, Log-Pfad, Auto-Update BcContainerHelper)
- Light/Dark Theme
- Vollständige Validierung mit Inline-Feedback (Field-Level)

## Phase 4 — Erweiterte Features

- Verfügbare Artifact-Versionen dynamisch via `Get-BCArtifactUrl` befüllen
- Multitenant-Support
- Memory/Isolation/DNS Settings
- TestToolkit / PerformanceToolkit Optionen
- BC-Symbole automatisch in VS Code Workspace ziehen

## Phase 5 — Polish

- Bulk-Aktionen (mehrere Container gleichzeitig stoppen)
- Auto-Cleanup alter ungenutzter Container
- Backup/Restore von Container-DBs
- Issue-Reporter mit GitHub-Template-Vorbefüllung
- MSI-Installer
- Auto-Update der App

## Spätere Ideen

- AL Project Wizard
- Multi-User-Setup auf Build-Servern
