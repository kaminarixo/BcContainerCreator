# Release-Prozess

Wie eine neue Version (`v1.0.x` / `v1.x.0` / `vX.0.0`) auf GitHub
veröffentlicht wird.

Kurzfassung: **Versionen bumpen → Branch + PR + squash-Merge → Tag pushen
→ Action erledigt den Rest.** Du musst nichts lokal bauen, kein ISCC
aufrufen, keinen Release manuell erstellen.

---

## Voraussetzungen (einmalig)

- Lokal `gh` installiert + eingeloggt (`gh auth status`)
- Lokal `git` und `dotnet` (10.0.x) installiert — nur falls du vor dem Tag
  noch `dotnet test` willst
- Inno Setup 6 brauchst du **nicht** — die Action hat das auf dem
  Runner vorinstalliert. Nur für lokale Setup-Bauten (Fallback unten).

---

## Standard-Release in 6 Schritten

Beispiel: `1.0.2` → `1.0.3`.

### 1. Version bumpen — nur EINE Datei

Die csproj ist die Single Source of Truth:

| Datei | Felder |
|---|---|
| [src/BcContainerCreator.App/BcContainerCreator.App.csproj](../src/BcContainerCreator.App/BcContainerCreator.App.csproj) | `<Version>`, `<AssemblyVersion>`, `<FileVersion>`, `<InformationalVersion>` |

`build/build-installer.ps1` liest die Version bei lokalen Bauten automatisch
aus der csproj; die Release-Action übergibt sie aus dem Tag. Das
`.iss`-Skript bekommt sie in beiden Fällen via `/DMyAppVersion` durchgereicht
(`#ifndef`-Guard) — dort ist nichts mehr zu pflegen.

### 2. Branch, Commit, PR, Squash-Merge

```powershell
git checkout -b chore/bump-1.0.3 main
# … editieren …
git add -A
git commit -m "chore: bump version to 1.0.3"
git push -u origin chore/bump-1.0.3
gh pr create --title "chore: bump version to 1.0.3" --body "Versions-Bump."
# Review + Merge:
gh pr merge --squash --delete-branch
```

### 3. Lokal main pullen

```powershell
git checkout main
git pull --ff-only origin main
```

### 4. Tag pushen

```powershell
git tag -a v1.0.3 -m "v1.0.3 — kurze Release-Beschreibung"
git push origin v1.0.3
```

Sobald der Tag auf `origin` ist, startet die Release-Action automatisch.

### 5. Action beobachten

```powershell
gh run watch --exit-status
# oder im Browser:  https://github.com/kaminarixo/BcContainerCreator/actions
```

Dauer auf `windows-latest`: ~2 Minuten (Restore + Tests + Publish + ISCC +
Release).

### 6. Release prüfen

```powershell
gh release view v1.0.3
```

Sollte `BcContainerCreator-Setup-1.0.3.exe` als Asset listen. Direktlink
für Downloader:

```
https://github.com/kaminarixo/BcContainerCreator/releases/tag/v1.0.3
```

Fertig.

---

## Fehlerfälle

### Tag versehentlich auf falschen Commit gesetzt

```powershell
git push --delete origin v1.0.3
git tag -d v1.0.3
git tag -a v1.0.3 -m "…"
git push origin v1.0.3
```

Der vorherige Action-Run wird dadurch nicht beendet — er muss in den
Actions-Log manuell gecancelt werden, sonst entsteht doppelter Asset-Ärger.
Wenn schon ein Release zum Tag existiert, vorher `gh release delete v1.0.3
--yes` ausführen.

### Action ist fehlgeschlagen

`gh run view <run-id> --log-failed` zeigt den Output des fehlgeschlagenen
Steps. Typische Ursachen:

- **Tests rot** auf CI, lokal grün: oft ein Admin-Kontext-Unterschied
  (CI-Runner sind elevated). Lösung wie bei
  `ApplyFixAsync_SwitchToWindowsMode_NonAdmin_ElevatesViaUac` (siehe Git-
  Historie zu PR #5): statische Helper hinter eine injizierbare Func<bool>
  / einen Test-Hook ziehen.
- **`dotnet format --verify-no-changes` rot** (Format-Check im PR-CI,
  [.github/workflows/ci.yml](../.github/workflows/ci.yml)): lokal
  `dotnet format` laufen lassen und committen. CRLF vs. LF ist das
  häufigste Symptom — die `.editorconfig` schreibt CRLF vor.
- **ISCC.exe-Fehler**: in der Regel ein .iss-Syntaxfehler nach Edit;
  lokales `pwsh build/build-installer.ps1` reproduziert das schneller als
  Action-Round-Trips.

### Tag schon vergeben (z. B. älterer Release)

Eine Version pro Tag. Nimm die nächste freie Patch-/Minor-Nummer und
bumpe entsprechend. Tags sind in `git ls-remote --tags origin` sichtbar.

---

## Lokaler Fallback (ohne Action)

Wenn die Action mal nicht funktioniert oder du offline einen Setup
brauchst:

```powershell
# einmalig: Inno Setup 6 installieren
winget install --exact --id JRSoftware.InnoSetup --silent

# Build:
powershell -NoProfile -ExecutionPolicy Bypass -File build/build-installer.ps1 -Version 1.0.3
# oder:
pwsh build/build-installer.ps1 -Version 1.0.3
```

Output: `dist/BcContainerCreator-Setup-1.0.3.exe`. Kann manuell als
Release-Asset hochgeladen werden:

```powershell
gh release create v1.0.3 dist/BcContainerCreator-Setup-1.0.3.exe `
  --title "v1.0.3" --generate-notes
```

---

## Versionierungs-Heuristik

- **Patch (`1.0.x`)**: Bugfixes, kleine UI-Politur, Test-Erweiterungen.
- **Minor (`1.x.0`)**: neue Features ohne Breaking Change am Settings-
  Format / Container-Metadaten-Schema.
- **Major (`x.0.0`)**: Schema-Bruch (Container-Metadata-Migrationen
  notwendig), ABI-Änderung am Core, oder bewusste Feature-Streichung.

Bis 2.0 gilt: Patch für Fixes und Politur, Minor für neue Features —
Major-Sprünge nur bei echtem Schema-/ABI-Bruch.
