using System.Security.Principal;
using BcContainerLauncher.Core.Docker;
using BcContainerLauncher.Core.Models;
using BcContainerLauncher.Core.PowerShell;
using Microsoft.Extensions.Logging;

namespace BcContainerLauncher.Core.Setup;

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
        "ps-version",
        "execution-policy",
        "nuget-provider",
        "psgallery-trust",
        "docker-installed",
        "docker-running",
        "docker-mode",
        "bccontainerhelper-installed",
        "legacy-navcontainerhelper"
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
        await Run(CheckPSVersionAsync).ConfigureAwait(false);
        await Run(CheckExecutionPolicyAsync).ConfigureAwait(false);
        await Run(CheckNuGetProviderAsync).ConfigureAwait(false);
        await Run(CheckPSGalleryTrustAsync).ConfigureAwait(false);
        await Run(CheckDockerInstalledAsync).ConfigureAwait(false);
        await Run(CheckDockerRunningAsync).ConfigureAwait(false);
        await Run(CheckDockerModeAsync).ConfigureAwait(false);
        await Run(CheckBcContainerHelperAsync).ConfigureAwait(false);
        await Run(CheckLegacyNavContainerHelperAsync).ConfigureAwait(false);

        return results;
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

    private async Task<CheckResult> CheckPSVersionAsync(CancellationToken ct)
    {
        var r = await _runner.ExecuteAsync("$PSVersionTable.PSVersion.ToString()", cancellationToken: ct).ConfigureAwait(false);
        var version = r.Objects.FirstOrDefault()?.ToString() ?? "unbekannt";
        return new CheckResult(
            Name: "PowerShell-Version",
            Status: r.Success ? CheckStatus.Ok : CheckStatus.Warning,
            Message: $"In-Process PowerShell: {version}",
            IsFixable: false);
    }

    private async Task<CheckResult> CheckExecutionPolicyAsync(CancellationToken ct)
    {
        var r = await _runner.ExecuteAsync(
            "(Get-ExecutionPolicy -Scope CurrentUser).ToString()",
            cancellationToken: ct).ConfigureAwait(false);

        var policy = r.Objects.FirstOrDefault()?.ToString() ?? "Undefined";
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

        var ok = r.Objects.Any(o => string.Equals(o?.ToString(), "yes", StringComparison.OrdinalIgnoreCase));
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
        var policy = r.Objects.FirstOrDefault()?.ToString() ?? "unknown";
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
            Message: ok ? "Docker-CLI im PATH." : "Docker Desktop fehlt — bitte herunterladen und installieren (Admin nötig).",
            IsFixable: false,
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

        var version = r.Objects.FirstOrDefault()?.ToString();
        var ok = !string.IsNullOrWhiteSpace(version);
        return new CheckResult(
            Name: "BcContainerHelper-Modul",
            Status: ok ? CheckStatus.Ok : CheckStatus.Warning,
            Message: ok ? $"Installiert ({version})." : "Nicht installiert — wird für Container-Erstellung benötigt.",
            IsFixable: !ok,
            FixId: "install-bccontainerhelper");
    }

    private async Task<CheckResult> CheckLegacyNavContainerHelperAsync(CancellationToken ct)
    {
        var r = await _runner.ExecuteAsync(
            $"if (Get-Module -ListAvailable -Name {Constants.LegacyNavContainerHelperModule}) {{ 'yes' }} else {{ 'no' }}",
            cancellationToken: ct).ConfigureAwait(false);

        var hasLegacy = r.Objects.Any(o => string.Equals(o?.ToString(), "yes", StringComparison.OrdinalIgnoreCase));
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
