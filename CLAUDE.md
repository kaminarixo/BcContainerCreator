# CLAUDE.md — BC Container Creator

Projekt-Kontext und Konventionen für künftige Claude-Code-Sessions in diesem Repo.

## Projekt

Windows-Desktop-App (.NET 10 / WPF), die Business-Central-Entwicklern und Teams eine GUI für die Erstellung und Verwaltung von Docker-Containern (`BcContainerHelper`) gibt — ohne manuelle PowerShell-Schritte.

## Tech-Stack (verbindlich)

- **.NET 10** (`net10.0-windows` für alle Projekte — Core inkl., wegen `WindowsPrincipal` und DPAPI)
- **WPF** mit MVVM (CommunityToolkit.Mvvm)
- **Externer PowerShell-Runner** — jedes Skript läuft in einem frischen `powershell.exe`-Subprozess (Windows PowerShell 5.1), headless ohne Konsolenfenster; stdout/stderr werden zeilenweise gestreamt
- **Serilog** (File + In-Memory-Sink für Live-Log)
- **Microsoft.Extensions.Hosting** (Generic Host als DI-Container)
- **xUnit + FluentAssertions 7.2.0 + Moq** für Tests (FluentAssertions ab 8.x ist kommerziell — bleib auf 7.x)

## Projektstruktur

```
src/BcContainerCreator.Core/   Class Library (UI-frei)
src/BcContainerCreator.App/    WPF .exe (asInvoker — Admin nur on-demand)
tests/BcContainerCreator.Core.Tests/
docs/ROADMAP.md
```

## Coding-Konventionen

- **Sprache:** Antworten und Code-Kommentare auf Deutsch (mit Umlauten — niemals ae/oe/ue/ss). Bezeichner Englisch.
- **MVVM strikt** — keine Logik in Code-Behind außer UI-Wiring.
- **Async/await** durchgehend, kein `.Result` / `.Wait()`.
- **CancellationToken** bei allen lang laufenden Operationen.
- **PowerShell-Aufrufe** nur über `IPowerShellRunner` (testbar via `FakePowerShellRunner`).
- **XML-Doc-Kommentare** an allen Public-APIs in Core.
- **Keine Magic Strings** — Konstanten in `Core/Constants.cs`.
- **Records** für Models (`ContainerCreateRequest`, `CheckResult`, `PSResult`).
- **Passwörter als `SecureString`** — nie als String im PS-Skript. Werden vom Runner über eine JSON-Datei im User-only-Runtime-Verzeichnis (`%LOCALAPPDATA%\BcContainerCreator\runtime`, siehe `RuntimePaths`) in den Subprozess gegeben (siehe `ContainerService.CreateContainerAsync`).
- **Container-Metadaten** — gespeicherte Passwörter werden via DPAPI (CurrentUser) verschlüsselt.
- **Conventional Commits**: `feat:`, `fix:`, `chore:`, `docs:`, `test:`, `refactor:`.

## Architektur-Prinzipien

- **Core ist UI-frei** — eine spätere CLI muss möglich sein, ohne Refactoring.
- **Externer PowerShell-Subprozess** — jedes Skript wird in einer frischen `powershell.exe` (Windows PowerShell 5.1) gestartet, headless ohne Konsolenfenster.
- **Aufrufe serialisiert** — `SemaphoreSlim` im Runner, damit sich stdout-Zeilen aus parallelen Aktionen nicht vermischen.
- **Parameter über JSON-Datei im User-only-Runtime-Verzeichnis** (`%LOCALAPPDATA%\BcContainerCreator\runtime` — Default-ACL beschränkt auf aktuellen User) — keine Argumente in der Prozesszeile, kein Klartext-Passwort in Logs.
- **Streaming-Output** — stdout/stderr werden zeilenweise via `IPowerShellRunner.OutputReceived` an UI/Logs gestreamt; UI marshalt mit `DispatcherProgress<T>`.
- **Stufenbasierter Progress** — `New-BcContainer` liefert keine Prozent-API; bekannte BcContainerHelper-Statuszeilen werden auf Etappen gemappt.
- **Fehler nie schlucken** — strukturiert loggen + UI-Feedback (DialogService).

## Domänen-Wissen

- `BcContainerHelper` ist das PSGallery-Modul; Hauptcommand `New-BcContainer` (~50 Parameter).
- Artifact-URL über `Get-BcArtifactUrl -type OnPrem|Sandbox -country <DE/W1/…> -version <latest/26/…>`.
- Auth: `Windows` oder `NavUserPassword` (mit `PSCredential`).
- Docker muss im **Windows-Container-Modus** laufen (CLI-Helper: `DockerCli.exe -SwitchDaemon`).
- Konflikt: altes `navcontainerhelper`-Modul muss vor `BcContainerHelper`-Nutzung entfernt sein — wird vom Preflight geprüft.
- Die Microsoft-Artifact-Endpunkte (`bcartifacts.blob.core.windows.net` bzw. der aktuelle CDN-Host, den `Get-BcArtifactUrl` liefert) müssen erreichbar sein — Proxy-Thema in Firmen-Netzwerken. Hinweis: das frühere `bcartifacts.azureedge.net` (Edgio-CDN) ist seit Anfang 2025 abgeschaltet.

## Wichtige Dateien

- `src/BcContainerCreator.Core/Containers/ContainerService.cs` — baut den `New-BcContainer`-Aufruf.
- `src/BcContainerCreator.Core/Containers/ContainerMetadataStore.cs` — Persistenz pro Container, Passwort via DPAPI-CurrentUser.
- `src/BcContainerCreator.Core/Setup/PreflightCheck.cs` — 13 Checks (Admin, Windows-Edition, PSVersion, ExecutionPolicy, NuGet, PSGallery, Docker × 3, BcContainerHelper, BcContainerHelper-Rechte, Legacy-Modul, externer PS- + BcContainerHelper-Smoke-Test).
- `src/BcContainerCreator.Core/Setup/SetupService.cs` — Fix-Aktionen pro Check.
- `src/BcContainerCreator.Core/PowerShell/PowerShellRunner.cs` — startet `powershell.exe`-Subprozess pro Aufruf, serialisiert via `SemaphoreSlim`, Parameter über JSON-Datei im Runtime-Verzeichnis (`RuntimePaths`).
- `src/BcContainerCreator.App/App.xaml.cs` — DI-Setup, Serilog-Konfiguration, MainWindow-Bootstrap.

## Build & Test

```powershell
dotnet build
dotnet test
dotnet format --verify-no-changes   # CI-Gate (ci.yml) — vor dem Push lokal 'dotnet format' laufen lassen
dotnet run --project src/BcContainerCreator.App   # läuft als Standard-User; Admin-Aktionen via UAC-Prompt
dotnet publish src/BcContainerCreator.App -c Release -r win-x64 -p:PublishSingleFile=true --self-contained false
```

CI: `.github/workflows/ci.yml` baut + testet jeden PR (inkl. Format-Check, Release-Build mit `TreatWarningsAsErrors`). Releases laufen über `.github/workflows/release.yml` bei Tag-Push (siehe `docs/RELEASE.md`).

## Funktionsumfang & Scope

Bereits umgesetzt (nicht mehr "out of scope"): Container-Verwaltung (Liste, Start, Stop, Löschen, Logs, Zugangsdaten-Popup), Settings-Tab, Installer + Release-Workflow.

Noch offen (siehe `docs/ROADMAP.md`): Profile/Presets, Health-Check pro Container, Auto-Update-Prüfung, Theme-Switching, CLI-Modus, Code-Signing des Installers.
