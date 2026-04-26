# BC Container Creator

Windows-Desktop-App (WPF, .NET 10), die per GUI Docker-Container für die Business-Central-Entwicklung erstellt und verwaltet — ohne dass man PowerShell oder `BcContainerHelper` direkt anfassen muss.

**Status:** Phase 1 (PoC mit tragfähiger Architektur). Container-Erstellung funktioniert; Container-Verwaltung kommt in Phase 2. Siehe [docs/ROADMAP.md](docs/ROADMAP.md).

---

## Quickstart

### Voraussetzungen

- Windows 10/11 mit Admin-Rechten
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Docker Desktop im **Windows-Container-Modus** (die App prüft das und kann umschalten)
- Optional: BC-Lizenz-Datei (`.flf` / `.bclicense`) — die App funktioniert auch ohne

### Bauen

```powershell
dotnet restore
dotnet build
```

### Tests

```powershell
dotnet test
```

### Starten (Debug)

```powershell
dotnet run --project src/BcContainerLauncher.App
```

Die App startet als Administrator (UAC-Prompt) — sonst kann sie weder Docker steuern noch PSGallery-Module installieren.

### Single-File-Publish

```powershell
dotnet publish src/BcContainerLauncher.App `
  -c Release -r win-x64 `
  -p:PublishSingleFile=true `
  --self-contained false
```

Output: `src/BcContainerLauncher.App/bin/Release/net10.0-windows/win-x64/publish/BcContainerLauncher.exe`

`--self-contained false` setzt voraus, dass auf dem Zielrechner die .NET-10-Runtime installiert ist. Für eine Distribution ohne Runtime-Voraussetzung: `--self-contained true` (Output ist dann ~70 MB).

---

## Architektur

```
src/
  BcContainerLauncher.Core/    Class Library (UI-frei, theoretisch CLI-fähig)
    PowerShell/                IPowerShellRunner mit persistentem Runspace
    Docker/                    IDockerService (CLI-Wrapper)
    Setup/                     IPreflightCheck + ISetupService
    Containers/                IContainerService (New-BcContainer-Wrapper)
    Models/                    Records: ContainerCreateRequest, CheckResult, …

  BcContainerLauncher.App/     WPF-Anwendung (.exe)
    ViewModels/                MVVM mit CommunityToolkit.Mvvm
    Views/                     UserControls pro Tab
    Services/                  DialogService, DispatcherProgress, PasswordBoxAssistant
    Logging/                   InMemoryLogSink für Live-Log-Tab
    Converters/                XAML-Value-Converter

tests/
  BcContainerLauncher.Core.Tests/
```

**Designprinzipien:**

- **Core ist UI-frei** — keine WPF-Abhängigkeit, eine spätere CLI ist möglich.
- **Persistenter PowerShell-Runspace** — `BcContainerHelper`-Modul wird einmalig geladen, nicht pro Aufruf (sonst je ~5s Overhead).
- **Streaming-Output** — alle PS-Streams (Information/Warning/Error/Verbose/Progress) werden live in die UI gepusht.
- **Cancellation überall** — alle lang laufenden Operationen nehmen `CancellationToken`.
- **Passwörter als `SecureString`** — niemals als Plain-String im Skript, sondern als injizierte Runspace-Variable.

---

## Decisions / Known Issues

- **`Microsoft.PowerShell.SDK` 7.6.1** ist mit .NET 10 kompatibel — kein Fallback auf `Process.Start("pwsh.exe", …)` nötig.
- **`pwsh.exe` ist nicht erforderlich.** Die App nutzt In-Process PowerShell via SDK; Windows PowerShell 5.1 muss aber als Host-Voraussetzung vorhanden sein (auf jedem Windows 10/11 default).
- **Core-TargetFramework `net10.0-windows`** statt `net10.0`: PowerShell.SDK zieht Windows-spezifische Abhängigkeiten (PSReadLine, PerformanceCounter), und der `WindowsPrincipal`-Admin-Check braucht Windows-API.
- **`FluentAssertions 7.2.0`** statt 8.x — ab 8.0.0 nicht mehr Apache-2.0, sondern kommerziell. 7.2.0 ist die letzte freie Version.
- **`requireAdministrator`** im Manifest erzwingt UAC. Lokal entwickeln: VS/Rider als Admin starten, sonst schlägt `dotnet run` mit Permission-Errors fehl.

---

## Was Phase 1 enthält

- Diagnose-Tab mit 10 Voraussetzungs-Checks und automatischen Fix-Aktionen
- Container-Erstellen-Tab mit Validierung, Live-Output und Cancellation
- Live-Log-Tab mit Filter, Copy, Save
- DI-Container, Serilog (File + In-Memory), persistenter PowerShell-Runspace
- 24 Unit-Tests (xUnit + FluentAssertions + Moq)

## Was Phase 1 NICHT enthält

- Verwaltung bestehender Container (Start/Stop/Remove) — Tab ist Placeholder
- Speicherbare Profile, Theme-Switching, Settings-Tab — Placeholder
- Auto-Updater, Installer

Siehe [docs/ROADMAP.md](docs/ROADMAP.md) für die geplante Roadmap.

---

## Logs

Logfiles unter `%ProgramData%\BcContainerLauncher\Logs\` — tagesrotierend, 14 Tage Retention.
