<div align="center">

<img src="src/BcContainerCreator.App/Assets/logo.png" alt="BC Container Creator Logo" width="160" />

# BC Container Creator

**Business-Central-Docker-Container per GUI erstellen und verwalten — ohne PowerShell, ohne `BcContainerHelper`-Knowhow.**

[![Latest Release](https://img.shields.io/github/v/release/kaminarixo/BcContainerCreator?label=Download&color=C8302A)](https://github.com/kaminarixo/BcContainerCreator/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows-lightgrey)]()

</div>

---

## Was ist das?

Ein Windows-Desktop-Tool, das BC-Entwicklern eine GUI gibt für alles, was sonst über `BcContainerHelper` per PowerShell läuft:

- **Voraussetzungen prüfen + automatisch fixen** (Docker, BcContainerHelper, ExecutionPolicy, PSGallery, Windows-Edition, …)
- **Container erstellen** mit Versions-Auswahl (BC LTS-Releases), MultiTenant, Memory-Limit, etc.
- **Container verwalten** — Liste mit Live-Status, Start, Stop, Löschen, Web-Client öffnen, Logs ansehen
- **Zugangsdaten-Popup** pro Container (URL, User, Passwort) — Passwort lokal DPAPI-verschlüsselt
- **Standard-User-Modus** — App startet ohne UAC, einzelne Admin-Aktionen werden on-demand elevated (lokaler Admin via UAC-Prompt)

Geschrieben für Teams, die von **NAV 2017 auf BC 28** migrieren und PowerShell-unsicher sind.

---

## Download &amp; Installation

[**→ Aktuellen Setup von GitHub Releases laden**](https://github.com/kaminarixo/BcContainerCreator/releases/latest)

1. `BcContainerCreator-Setup-x.y.z.exe` herunterladen
2. Doppelklick → UAC-Prompt mit lokalem Admin (z. B. `.\admin`) bestätigen
3. .NET-10-Desktop-Runtime-Check — fehlt sie, öffnet das Setup automatisch die Download-Seite
4. Pfad-Auswahl, Start-Menü-Eintrag, optional Desktop-Icon
5. Fertig

### Voraussetzungen auf dem Zielrechner

- **Windows 10/11 Pro / Enterprise / Education** (Home unterstützt keine Windows-Container — der Diagnose-Tab markiert das)
- **.NET 10 Desktop Runtime (x64)** — der Installer prüft das und verlinkt den Download
- **Docker Desktop im Windows-Container-Modus** — der Diagnose-Tab kann es per UAC-Prompt installieren / umschalten

---

## Features

### Diagnose

11 Voraussetzungs-Checks mit fixbaren Aktionen:

- Ausführungs-Kontext (Admin / Standard-User)
- Windows-Edition (Pro/Enterprise/Education vs. Home)
- PowerShell-Version + ExecutionPolicy
- NuGet-Provider, PSGallery-Trust
- Docker installiert / läuft / im Windows-Modus
- BcContainerHelper-Modul installiert
- Kein konkurrierendes Legacy-Modul (`navcontainerhelper`)

Fixes laufen mit `Microsoft.PowerShell.PSResourceGet` (modern, ohne den alten PowerShellGet-1.0.0.1-Bug unter PS7-In-Process). Wo nötig wird automatisch via UAC eskaliert.

### Container erstellen

- Versions-Dropdown mit `latest` + den letzten BC-LTS-Majors (28, 27, 26, …) inkl. konkret aufgelöstem Build
- Country-Dropdown (DE/W1/AT/CH/US/…)
- Auth-Typ: `NavUserPassword` (Default) oder `Windows`
- Username default = aktueller Windows-User; Passwort mit Show/Hide-Toggle
- Optionaler Lizenz-Pfad
- Erweiterte Optionen: MultiTenant, TestToolkit, Memory-Limit, Isolation-Mode
- Live-Output rechts mit Brand-Spinner-Overlay
- Schließen während Erstellung läuft → Confirm-Dialog mit Cancel-Option

### Container verwalten

- Auto-Refresh (10s) der Container-Liste
- Pro Container: **Web öffnen** (`http://&lt;name&gt;/BC?tenant=default`), **Info** (Zugangsdaten-Popup), **Logs** (separates Fenster mit tail-Selector), **Start / Stop**, **Löschen**

### Logging &amp; Settings

- File-Logs unter `%ProgramData%\BcContainerCreator\Logs\` — täglich rotierend, 14 Tage Retention
- Live-Log-Tab mit Copy / Save / Auto-Scroll
- Settings-Tab mit App-Version, OS-Info, Log-Folder-Open

---

## Architektur

```
src/
  BcContainerCreator.Core/    Class Library — UI-frei, eine spätere CLI ist möglich
    PowerShell/                IPowerShellRunner mit persistentem Runspace
    Docker/                    IDockerService (CLI-Wrapper)
    Setup/                     IPreflightCheck + ISetupService + IElevationService
    Containers/                IContainerService + IContainerMetadataStore (DPAPI)
    Models/                    Records: ContainerCreateRequest, CheckResult, …

  BcContainerCreator.App/     WPF-Anwendung (.exe)
    ViewModels/                MVVM mit CommunityToolkit.Mvvm
    Views/                     UserControls + Modal-Windows
    Services/                  DialogService, DispatcherProgress, PasswordBoxAssistant

tests/
  BcContainerCreator.Core.Tests/    27 xUnit-Tests
```

**Kern-Entscheidungen:**

- **Microsoft.PowerShell.SDK 7.6** in-process — kein externer `pwsh.exe` nötig.
- **Persistenter Runspace** — BcContainerHelper-Modul wird einmalig geladen.
- **PSResourceGet-Bootstrap** für PSGallery-Operations — umgeht den `$script:IsWindows`-Bug von PowerShellGet 1.0.0.1 unter PS7.
- **Passwörter als `SecureString`** im Code; im Container-Metadata-Store via DPAPI-CurrentUser verschlüsselt.
- **`asInvoker`-Manifest** — App läuft als Standard-User. Admin nur on-demand über `Verb=runas`.

---

## Selbst bauen

### Voraussetzungen

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Optional für den Installer: [Inno Setup 6](https://jrsoftware.org/isinfo.php) — `winget install --exact --id JRSoftware.InnoSetup --silent`

### Quick build

```powershell
dotnet restore
dotnet build
dotnet test
dotnet run --project src/BcContainerCreator.App
```

### Single-File-Publish

```powershell
dotnet publish src/BcContainerCreator.App `
  -c Release -r win-x64 `
  -p:PublishSingleFile=true `
  --self-contained false
```

### Bundle-Installer (Setup.exe)

```powershell
pwsh build/build-installer.ps1
# optional: -Version 1.2.3
```

→ `dist/BcContainerCreator-Setup-<version>.exe` (≈12 MB)

---

## Roadmap

Siehe [docs/ROADMAP.md](docs/ROADMAP.md) für die geplanten Phasen (Container-Verwaltung-Erweiterungen, Profile, Auto-Update, MSI-Distribution, …).

---

## Lizenz

[MIT](LICENSE) — Copyright © 2026 Thomas Scharf
