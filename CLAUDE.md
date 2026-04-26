# CLAUDE.md — BC Container Creator

Projekt-Kontext und Konventionen für künftige Claude-Code-Sessions in diesem Repo.

## Projekt

Windows-Desktop-App (.NET 10 / WPF), die NAV/BC-Entwicklern eine GUI für die Erstellung und Verwaltung von Docker-Containern (`BcContainerHelper`) gibt. Hintergrund: Migration NAV 2017 → BC 28, Cutover November 2026, Team ist PowerShell-unsicher.

## Tech-Stack (verbindlich)

- **.NET 10** (`net10.0-windows` für alle Projekte — Core inkl., wegen PowerShell-SDK-Deps und `WindowsPrincipal`)
- **WPF** mit MVVM (CommunityToolkit.Mvvm)
- **Microsoft.PowerShell.SDK 7.6.1** — In-Process PowerShell, kein Fallback nötig
- **Serilog** (File + In-Memory-Sink für Live-Log)
- **Microsoft.Extensions.Hosting** (Generic Host als DI-Container)
- **xUnit + FluentAssertions 7.2.0 + Moq** für Tests (FluentAssertions ab 8.x ist kommerziell — bleib auf 7.x)

## Projektstruktur

```
src/BcContainerLauncher.Core/   Class Library (UI-frei)
src/BcContainerLauncher.App/    WPF .exe (requireAdministrator)
tests/BcContainerLauncher.Core.Tests/
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
- **Passwörter als `SecureString`** — nie als String im PS-Skript, sondern als Runspace-Variable injizieren (siehe `ContainerService.CreateContainerAsync`).
- **Conventional Commits**: `feat:`, `fix:`, `chore:`, `docs:`, `test:`, `refactor:`.

## Architektur-Prinzipien

- **Core ist UI-frei** — eine spätere CLI muss möglich sein, ohne Refactoring.
- **PowerShell-Runspace persistent** — `BcContainerHelper`-Module-Load dauert ~5s, deshalb Singleton-Runspace via DI.
- **Streaming-Output** — alle PS-Streams via `IPowerShellRunner.OutputReceived` an UI; UI marshalt mit `DispatcherProgress<T>`.
- **Fehler nie schlucken** — strukturiert loggen + UI-Feedback (DialogService).

## Domänen-Wissen

- `BcContainerHelper` ist das PSGallery-Modul; Hauptcommand `New-BcContainer` (~50 Parameter).
- Artifact-URL über `Get-BcArtifactUrl -type OnPrem|Sandbox -country <DE/W1/…> -version <latest/26/…>`.
- Auth: `Windows` oder `NavUserPassword` (mit `PSCredential`).
- Docker muss im **Windows-Container-Modus** laufen (CLI-Helper: `DockerCli.exe -SwitchDaemon`).
- Konflikt: altes `navcontainerhelper`-Modul muss vor `BcContainerHelper`-Nutzung entfernt sein — wird vom Preflight geprüft.
- `bcartifacts.azureedge.net` muss erreichbar sein (Proxy-Thema in Firmen-Netzwerken).

## Wichtige Dateien

- `src/BcContainerLauncher.Core/Containers/ContainerService.cs` — baut den `New-BcContainer`-Aufruf.
- `src/BcContainerLauncher.Core/Setup/PreflightCheck.cs` — 10 Checks (Admin, PSVersion, ExecutionPolicy, NuGet, PSGallery, Docker × 3, BcContainerHelper, Legacy-Modul).
- `src/BcContainerLauncher.Core/Setup/SetupService.cs` — Fix-Aktionen pro Check.
- `src/BcContainerLauncher.Core/PowerShell/PowerShellRunner.cs` — persistenter Runspace, Stream-Subscriptions, Cancellation via `BeginStop`.
- `src/BcContainerLauncher.App/App.xaml.cs` — DI-Setup, Serilog-Konfiguration, MainWindow-Bootstrap.

## Build & Test

```powershell
dotnet build
dotnet test
dotnet run --project src/BcContainerLauncher.App   # als Admin starten
dotnet publish src/BcContainerLauncher.App -c Release -r win-x64 -p:PublishSingleFile=true --self-contained false
```

## Out of Scope für Phase 1 (siehe ROADMAP)

- Container-Verwaltung (Start/Stop/Remove)
- Profile/Presets, Settings-Tab, Theme-Switching
- Auto-Updater, Installer
