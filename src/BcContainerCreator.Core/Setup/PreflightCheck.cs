using System.Security.Principal;
using BcContainerCreator.Core.Docker;
using BcContainerCreator.Core.Models;
using BcContainerCreator.Core.PowerShell;
using Microsoft.Extensions.Logging;

namespace BcContainerCreator.Core.Setup;

/// <summary>
/// Standard-Implementierung mit folgenden Checks (in dieser Reihenfolge):
/// Admin-Rechte, PowerShell-Version, ExecutionPolicy, NuGet-Provider,
/// PSGallery-Trust, Docker installiert, Docker läuft, Docker-Modus,
/// BcContainerHelper installiert, Legacy-Modul nicht installiert.
/// </summary>
public sealed class PreflightCheck : IPreflightCheck
{
    private readonly IPowerShellRunner _runner;
    private readonly IDockerService _docker;
    private readonly ILogger<PreflightCheck> _logger;

    private static readonly string[] CheckIds =
    [
        "admin",
        "windows-edition",
        "ps-version",
        "execution-policy",
        "nuget-provider",
        "psgallery-trust",
        "docker-installed",
        "docker-running",
        "docker-mode",
        "bccontainerhelper-installed",
        "bccontainerhelper-permissions",
        "legacy-navcontainerhelper",
        "external-ps-smoketest"
    ];

    public PreflightCheck(IPowerShellRunner runner, IDockerService docker, ILogger<PreflightCheck> logger)
    {
        _runner = runner;
        _docker = docker;
        _logger = logger;
    }

    public IReadOnlyList<string> GetCheckIds() => CheckIds;

    public async Task<IReadOnlyList<CheckResult>> RunAllAsync(
        IProgress<CheckResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<CheckResult>();

        async Task Run(Func<CancellationToken, Task<CheckResult>> check)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var r = await check(cancellationToken).ConfigureAwait(false);
            results.Add(r);
            progress?.Report(r);
        }

        await Run(CheckAdminAsync).ConfigureAwait(false);
        await Run(CheckWindowsEditionAsync).ConfigureAwait(false);
        await Run(CheckPSVersionAsync).ConfigureAwait(false);
        await Run(CheckExecutionPolicyAsync).ConfigureAwait(false);
        await Run(CheckNuGetProviderAsync).ConfigureAwait(false);
        await Run(CheckPSGalleryTrustAsync).ConfigureAwait(false);
        await Run(CheckDockerInstalledAsync).ConfigureAwait(false);
        await Run(CheckDockerRunningAsync).ConfigureAwait(false);
        await Run(CheckDockerModeAsync).ConfigureAwait(false);
        await Run(CheckBcContainerHelperAsync).ConfigureAwait(false);
        await Run(CheckBcContainerHelperPermissionsAsync).ConfigureAwait(false);
        await Run(CheckLegacyNavContainerHelperAsync).ConfigureAwait(false);
        await Run(CheckExternalPSSmokeAsync).ConfigureAwait(false);

        return results;
    }

    /// <summary>
    /// Smoke-Test der externen Windows-PowerShell-Pipeline. Ruft im
    /// powershell.exe-Subprozess die typischen BcContainerHelper-Voraussetzungen
    /// ab — wenn das hier durchläuft, läuft auch New-BcContainer durch.
    /// </summary>
    private async Task<CheckResult> CheckExternalPSSmokeAsync(CancellationToken ct)
    {
        const string script = """
            Write-Information ("PSVersion:        {0}" -f $PSVersionTable.PSVersion)
            Write-Information ("Is64BitProcess:   {0}" -f [Environment]::Is64BitProcess)
            Write-Information ("PSHOME:           {0}" -f $PSHOME)
            Write-Information ("TEMP:             {0}" -f $env:TEMP)

            $addType = Get-Command Add-Type -ErrorAction SilentlyContinue
            if ($addType) {
                Write-Information ("Add-Type:         {0}" -f $addType.Source)
            } else {
                throw 'Add-Type ist nicht verfuegbar.'
            }

            Add-Type -AssemblyName System.IO.Compression.FileSystem
            Write-Information 'System.IO.Compression.FileSystem geladen.'

            Import-Module BcContainerHelper -Force -ErrorAction Stop
            $bcch = (Get-Module BcContainerHelper).Version
            Write-Information ("BcContainerHelper version: {0}" -f $bcch)

            $url = Get-BcArtifactUrl -type Sandbox -country DE -select Latest -ErrorAction Stop
            Write-Information ("Sandbox/DE/Latest: {0}" -f $url)
            """;

        var result = await _runner.ExecuteAsync(script, cancellationToken: ct).ConfigureAwait(false);
        if (result.Success)
        {
            return new CheckResult(
                Name: "Externe PowerShell + BcContainerHelper",
                Status: CheckStatus.Ok,
                Message: "powershell.exe-Smoketest läuft, BcContainerHelper lädt, Get-BcArtifactUrl erreichbar.");
        }

        var errorJoined = string.Join(" | ", result.Errors.Take(3));
        return new CheckResult(
            Name: "Externe PowerShell + BcContainerHelper",
            Status: CheckStatus.Failed,
            Message: $"Smoketest fehlgeschlagen (ExitCode {result.ExitCode}): {errorJoined}",
            IsFixable: false);
    }

    private Task<CheckResult> CheckAdminAsync(CancellationToken _)
    {
        // Informativ. Die App läuft als asInvoker — nur einzelne Aktionen
        // (Docker-Modus-Switch) brauchen wirklich Admin und werden dann
        // on-demand via Verb=runas elevated.
        var isAdmin = AdminContext.IsCurrentProcessAdmin;
        return Task.FromResult(new CheckResult(
            Name: "Ausführungs-Kontext",
            Status: CheckStatus.Ok,
            Message: isAdmin
                ? "Prozess läuft mit Admin-Rechten."
                : "Standard-User. Admin-pflichtige Aktionen fragen via UAC nach (lokaler Admin, z. B. .\\admin)."));
    }

    private async Task<CheckResult> CheckWindowsEditionAsync(CancellationToken ct)
    {
        // EditionID kommt aus der Registry und ist deterministischer als
        // Get-CimInstance Win32_OperatingSystem.Caption (lokalisiert).
        // ACHTUNG: ProductName liefert auf Windows 11 aus Kompatibilitätsgründen
        // immer noch "Windows 10 …". Korrekte 10-vs-11-Unterscheidung geht nur
        // über CurrentBuild (>= 22000 → Windows 11).
        const string script = """
            $key = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
            $props    = Get-ItemProperty -Path $key -ErrorAction SilentlyContinue
            $edition  = $props.EditionID
            $product  = $props.ProductName
            $build    = $props.CurrentBuildNumber
            "$edition|$product|$build"
            """;
        var result = await _runner.ExecuteAsync(script, cancellationToken: ct).ConfigureAwait(false);
        var raw = result.Objects.FirstOrDefault() ?? string.Empty;
        var parts = raw.Split('|', 3);
        var edition = parts.Length > 0 ? parts[0] : string.Empty;
        var product = parts.Length > 1 ? parts[1] : string.Empty;
        var buildStr = parts.Length > 2 ? parts[2] : string.Empty;

        // Windows-11-Patch: ProductName ist registry-mäßig auf "Windows 10 …"
        // eingefroren — bei Build >= 22000 zeigen wir "Windows 11 …" an.
        if (int.TryParse(buildStr, out var build) && build >= 22000
            && product.Contains("Windows 10", StringComparison.OrdinalIgnoreCase))
        {
            product = product.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase);
        }

        // Pro / Enterprise / Education / ServerStandard etc. = Hyper-V verfügbar
        // → echte Windows-Container möglich.
        // Core / Home* = nur WSL2-Backend → Linux-Container, BC läuft NICHT.
        var supportsWindowsContainers = edition.Contains("Pro", StringComparison.OrdinalIgnoreCase)
            || edition.Contains("Enterprise", StringComparison.OrdinalIgnoreCase)
            || edition.Contains("Education", StringComparison.OrdinalIgnoreCase)
            || edition.Contains("Server", StringComparison.OrdinalIgnoreCase)
            || edition.Contains("Workstation", StringComparison.OrdinalIgnoreCase);
        var isHome = edition.Contains("Core", StringComparison.OrdinalIgnoreCase)
            || edition.Contains("Home", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(edition))
        {
            return new CheckResult(
                Name: "Windows-Edition",
                Status: CheckStatus.Warning,
                Message: "Edition konnte nicht ausgelesen werden.");
        }

        if (isHome || !supportsWindowsContainers)
        {
            return new CheckResult(
                Name: "Windows-Edition",
                Status: CheckStatus.Failed,
                Message: $"{product} ({edition}). Windows-Container brauchen Pro/Enterprise/Education — Home unterstützt nur Linux-Container, BC läuft damit nicht.",
                IsFixable: false,
                HelpUrl: "https://docs.docker.com/desktop/install/windows-install/#system-requirements");
        }

        return new CheckResult(
            Name: "Windows-Edition",
            Status: CheckStatus.Ok,
            Message: $"{product} ({edition}) — Windows-Container unterstützt.");
    }

    private async Task<CheckResult> CheckPSVersionAsync(CancellationToken ct)
    {
        var r = await _runner.ExecuteAsync(
            "Write-Output $PSVersionTable.PSVersion.ToString()",
            cancellationToken: ct).ConfigureAwait(false);
        var version = r.Objects.FirstOrDefault() ?? "unbekannt";
        return new CheckResult(
            Name: "PowerShell-Version",
            Status: r.Success ? CheckStatus.Ok : CheckStatus.Warning,
            Message: $"Windows PowerShell (extern): {version}",
            IsFixable: false);
    }

    private async Task<CheckResult> CheckExecutionPolicyAsync(CancellationToken ct)
    {
        var r = await _runner.ExecuteAsync(
            "(Get-ExecutionPolicy -Scope CurrentUser).ToString()",
            cancellationToken: ct).ConfigureAwait(false);

        var policy = r.Objects.FirstOrDefault() ?? "Undefined";
        var isOk = policy is "RemoteSigned" or "Unrestricted" or "Bypass";
        return new CheckResult(
            Name: "ExecutionPolicy (CurrentUser)",
            Status: isOk ? CheckStatus.Ok : CheckStatus.Warning,
            Message: $"Aktuell: {policy}{(isOk ? "" : " — empfohlen: RemoteSigned")}",
            IsFixable: !isOk,
            FixId: "set-execution-policy");
    }

    private async Task<CheckResult> CheckNuGetProviderAsync(CancellationToken ct)
    {
        var r = await _runner.ExecuteAsync(
            "if (Get-PackageProvider -Name NuGet -ListAvailable -ErrorAction SilentlyContinue) { 'yes' } else { 'no' }",
            cancellationToken: ct).ConfigureAwait(false);

        var ok = r.Objects.Any(o => string.Equals(o, "yes", StringComparison.OrdinalIgnoreCase));
        return new CheckResult(
            Name: "NuGet-PackageProvider",
            Status: ok ? CheckStatus.Ok : CheckStatus.Warning,
            Message: ok ? "Installiert." : "Fehlt — wird für PSGallery benötigt.",
            IsFixable: !ok,
            FixId: "install-nuget-provider");
    }

    private async Task<CheckResult> CheckPSGalleryTrustAsync(CancellationToken ct)
    {
        // Wir prüfen primär Microsoft.PowerShell.PSResourceGet (modern), weil
        // unser trust-psgallery-Fix dort schreibt. Fallback auf das Legacy-
        // Get-PSRepository, falls PSResourceGet noch nicht installiert ist —
        // dann kann zumindest der Trust-Status der alten PSGallery-Welt gemeldet
        // werden.
        const string script = """
            $status = 'unknown'
            $rg = Get-Command Get-PSResourceRepository -ErrorAction SilentlyContinue
            if ($rg) {
                $repo = Get-PSResourceRepository -Name PSGallery -ErrorAction SilentlyContinue
                if ($repo) { $status = if ($repo.Trusted) { 'Trusted' } else { 'Untrusted' } }
            }
            if ($status -eq 'unknown') {
                $legacy = Get-Command Get-PSRepository -ErrorAction SilentlyContinue
                if ($legacy) {
                    $repo = Get-PSRepository -Name PSGallery -ErrorAction SilentlyContinue
                    if ($repo) { $status = $repo.InstallationPolicy }
                }
            }
            $status
            """;

        var r = await _runner.ExecuteAsync(script, cancellationToken: ct).ConfigureAwait(false);
        var policy = r.Objects.FirstOrDefault() ?? "unknown";
        var ok = string.Equals(policy, "Trusted", StringComparison.OrdinalIgnoreCase);
        return new CheckResult(
            Name: "PSGallery vertrauenswürdig",
            Status: ok ? CheckStatus.Ok : CheckStatus.Warning,
            Message: ok ? "Trusted (PSResourceGet)." : $"Aktuell: {policy} — Trust empfohlen für stille Installs.",
            IsFixable: !ok,
            FixId: "trust-psgallery");
    }

    private async Task<CheckResult> CheckDockerInstalledAsync(CancellationToken ct)
    {
        var ok = await _docker.IsInstalledAsync(ct).ConfigureAwait(false);
        return new CheckResult(
            Name: "Docker installiert",
            Status: ok ? CheckStatus.Ok : CheckStatus.Failed,
            Message: ok
                ? "Docker-CLI im PATH."
                : "Docker Desktop fehlt — Fix installiert per winget (Admin nötig).",
            IsFixable: !ok,
            FixId: ok ? null : "install-docker-desktop",
            RequiresAdminForFix: !ok,
            HelpUrl: "https://docs.docker.com/desktop/install/windows-install/");
    }

    private async Task<CheckResult> CheckDockerRunningAsync(CancellationToken ct)
    {
        var ok = await _docker.IsRunningAsync(ct).ConfigureAwait(false);
        return new CheckResult(
            Name: "Docker-Daemon läuft",
            Status: ok ? CheckStatus.Ok : CheckStatus.Failed,
            Message: ok ? "Daemon erreichbar." : "Docker Desktop scheint nicht zu laufen.",
            IsFixable: false);
    }

    private async Task<CheckResult> CheckDockerModeAsync(CancellationToken ct)
    {
        var mode = await _docker.GetContainerModeAsync(ct).ConfigureAwait(false);
        return mode switch
        {
            ContainerMode.Windows => new CheckResult("Docker im Windows-Modus", CheckStatus.Ok, "Windows-Container aktiv."),
            ContainerMode.Linux => new CheckResult("Docker im Windows-Modus", CheckStatus.Failed,
                "Aktuell: Linux-Container — BC-Container brauchen Windows-Modus. Fix erfordert UAC (lokaler Admin).",
                IsFixable: true, FixId: "switch-to-windows-mode", RequiresAdminForFix: true),
            _ => new CheckResult("Docker im Windows-Modus", CheckStatus.Warning, "Modus konnte nicht erkannt werden.")
        };
    }

    private async Task<CheckResult> CheckBcContainerHelperAsync(CancellationToken ct)
    {
        var r = await _runner.ExecuteAsync(
            $"(Get-Module -ListAvailable -Name {Constants.BcContainerHelperModule}).Version | Select-Object -First 1 | ForEach-Object {{ $_.ToString() }}",
            cancellationToken: ct).ConfigureAwait(false);

        var version = r.Objects.FirstOrDefault();
        var ok = !string.IsNullOrWhiteSpace(version);
        return new CheckResult(
            Name: "BcContainerHelper-Modul",
            Status: ok ? CheckStatus.Ok : CheckStatus.Warning,
            Message: ok ? $"Installiert ({version})." : "Nicht installiert — wird für Container-Erstellung benötigt.",
            IsFixable: !ok,
            FixId: "install-bccontainerhelper");
    }

    /// <summary>
    /// Prüft via <c>Check-BcContainerHelperPermissions</c>, ob der aktuelle User
    /// Schreibrechte auf <c>%ProgramData%\BcContainerHelper</c>, die hosts-Datei
    /// und docker-CLI hat. Das Cmdlet ist Teil von BcContainerHelper; bei
    /// Warnings (fehlende Rechte) bieten wir einen elevated Fix an, der
    /// <c>Check-BcContainerHelperPermissions -Fix</c> ausführt.
    /// </summary>
    private async Task<CheckResult> CheckBcContainerHelperPermissionsAsync(CancellationToken ct)
    {
        const string script = """
            Import-Module BcContainerHelper -ErrorAction SilentlyContinue
            if (-not (Get-Command Check-BcContainerHelperPermissions -ErrorAction SilentlyContinue)) {
                Write-Output 'cmdlet-missing'
                exit 0
            }
            $warnings = New-Object System.Collections.ArrayList
            try {
                Check-BcContainerHelperPermissions -ErrorAction Stop -WarningVariable warnings -WarningAction SilentlyContinue | Out-Null
            } catch {
                Write-Output ('error: ' + $_.Exception.Message)
                exit 0
            }
            if ($warnings.Count -gt 0) {
                Write-Output ('needs-fix: ' + (($warnings | ForEach-Object { $_.ToString() }) -join ' | '))
            } else {
                Write-Output 'ok'
            }
            """;

        var r = await _runner.ExecuteAsync(script, cancellationToken: ct).ConfigureAwait(false);
        var line = r.Objects.FirstOrDefault() ?? "unknown";

        const string name = "BcContainerHelper-Berechtigungen";
        if (string.Equals(line, "cmdlet-missing", StringComparison.OrdinalIgnoreCase))
        {
            return new CheckResult(
                Name: name,
                Status: CheckStatus.Warning,
                Message: "Check-Cmdlet nicht verfügbar — Modul fehlt oder lädt nicht. Erst BcContainerHelper installieren.",
                IsFixable: false);
        }
        if (string.Equals(line, "ok", StringComparison.OrdinalIgnoreCase))
        {
            return new CheckResult(
                Name: name,
                Status: CheckStatus.Ok,
                Message: "ProgramData, hosts und Docker-CLI für aktuellen User schreibbar.");
        }
        if (line.StartsWith("needs-fix:", StringComparison.OrdinalIgnoreCase))
        {
            var detail = line["needs-fix:".Length..].Trim();
            return new CheckResult(
                Name: name,
                Status: CheckStatus.Warning,
                Message: $"Fehlende Rechte: {Truncate(detail, 240)}",
                IsFixable: true,
                FixId: "fix-bccontainerhelper-permissions",
                RequiresAdminForFix: true);
        }
        // 'error: ...' oder unerwartet
        return new CheckResult(
            Name: name,
            Status: CheckStatus.Failed,
            Message: Truncate(line, 240),
            IsFixable: true,
            FixId: "fix-bccontainerhelper-permissions",
            RequiresAdminForFix: true);
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "…";

    private async Task<CheckResult> CheckLegacyNavContainerHelperAsync(CancellationToken ct)
    {
        var r = await _runner.ExecuteAsync(
            $"if (Get-Module -ListAvailable -Name {Constants.LegacyNavContainerHelperModule}) {{ 'yes' }} else {{ 'no' }}",
            cancellationToken: ct).ConfigureAwait(false);

        var hasLegacy = r.Objects.Any(o => string.Equals(o, "yes", StringComparison.OrdinalIgnoreCase));
        return new CheckResult(
            Name: "Kein Legacy-Modul",
            Status: hasLegacy ? CheckStatus.Warning : CheckStatus.Ok,
            Message: hasLegacy
                ? $"{Constants.LegacyNavContainerHelperModule} installiert — kann Konflikte verursachen."
                : "Kein konkurrierendes Modul gefunden.",
            IsFixable: hasLegacy,
            FixId: "remove-legacy-module");
    }
}
