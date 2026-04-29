using BcContainerCreator.Core.Docker;
using BcContainerCreator.Core.PowerShell;
using Microsoft.Extensions.Logging;

namespace BcContainerCreator.Core.Setup;

/// <summary>
/// Setzt einzelne Voraussetzungen instand. Jede Fix-Aktion ist idempotent
/// und sicher mehrfach ausführbar.
/// </summary>
public sealed class SetupService : ISetupService
{
    private readonly IPowerShellRunner _runner;
    private readonly IDockerService _docker;
    private readonly IElevationService _elevation;
    private readonly ILogger<SetupService> _logger;
    private readonly Func<bool> _isCurrentProcessAdmin;

    private static readonly Dictionary<string, string> Fixes = new()
    {
        ["set-execution-policy"] = "ExecutionPolicy auf RemoteSigned setzen",
        ["install-nuget-provider"] = "NuGet-PackageProvider installieren",
        ["trust-psgallery"] = "PSGallery als vertrauenswürdig markieren",
        ["install-bccontainerhelper"] = "BcContainerHelper aus PSGallery installieren",
        ["remove-legacy-module"] = "Legacy-Modul navcontainerhelper entfernen",
        ["switch-to-windows-mode"] = "Docker auf Windows-Container-Modus umschalten",
        ["install-docker-desktop"] = "Docker Desktop installieren (via winget)",
        ["fix-bccontainerhelper-permissions"] = "BcContainerHelper-Rechte (ProgramData, hosts, Docker) reparieren"
    };

    public SetupService(
        IPowerShellRunner runner,
        IDockerService docker,
        IElevationService elevation,
        ILogger<SetupService> logger,
        Func<bool>? isCurrentProcessAdmin = null)
    {
        _runner = runner;
        _docker = docker;
        _elevation = elevation;
        _logger = logger;
        // Default-Probe gegen den statischen Helper. Tests können eine eigene
        // Lambda injizieren, damit das Verhalten unabhängig davon ist, ob der
        // Test-Runner selbst elevated läuft (auf GitHub-Actions-Windows-Runnern
        // schlug der frühere Test sonst fehl).
        _isCurrentProcessAdmin = isCurrentProcessAdmin ?? (() => AdminContext.IsCurrentProcessAdmin);
    }

    public IReadOnlyDictionary<string, string> AvailableFixes => Fixes;

    public async Task<bool> ApplyFixAsync(string fixId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixId);
        _logger.LogInformation("Fix {FixId} wird angewendet", fixId);

        if (fixId == "install-docker-desktop")
        {
            // Always elevated — Docker Desktop installiert sich systemweit.
            return await InstallDockerDesktopElevatedAsync(cancellationToken).ConfigureAwait(false);
        }

        if (fixId == "fix-bccontainerhelper-permissions")
        {
            return await FixBcContainerHelperPermissionsElevatedAsync(cancellationToken).ConfigureAwait(false);
        }

        if (fixId == "switch-to-windows-mode")
        {
            // DockerCli.exe -SwitchDaemon braucht Admin. Wenn die App selbst als
            // Standard-User läuft, wird der UAC-Prompt aufgehen — der User gibt
            // den lokalen Admin (.\admin) ein.
            if (_isCurrentProcessAdmin())
            {
                return await _docker.SwitchToWindowsModeAsync(cancellationToken).ConfigureAwait(false);
            }

            const string dockerCli = @"C:\Program Files\Docker\Docker\DockerCli.exe";
            return await _elevation.RunElevatedAsync(
                fileName: dockerCli,
                arguments: "-SwitchDaemon",
                timeout: TimeSpan.FromMinutes(2),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        var script = GetFixScript(fixId);
        var result = await _runner.ExecuteAsync(script, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            _logger.LogError("Fix {FixId} fehlgeschlagen: {Errors}", fixId, string.Join("; ", result.Errors));
        }
        return result.Success;
    }

    /// <summary>
    /// Ruft <c>Check-BcContainerHelperPermissions -Fix</c> elevated auf. Das
    /// Cmdlet repariert ACLs auf <c>%ProgramData%\BcContainerHelper</c>, fügt
    /// den User der docker-Group hinzu und gibt ihm hosts-Schreibrechte.
    /// Skript-Datei wird nach Lauf wieder gelöscht; ein Read-Host hält das
    /// Fenster auf, damit der User Output sehen kann.
    /// </summary>
    private async Task<bool> FixBcContainerHelperPermissionsElevatedAsync(CancellationToken ct)
    {
        var tempScript = Path.Combine(Path.GetTempPath(), $"bccl-bcch-perms-{Guid.NewGuid():N}.ps1");
        const string script = """
            $ErrorActionPreference = 'Stop'
            Write-Host '== BC Container Creator: Check-BcContainerHelperPermissions -Fix =='
            try {
                Import-Module BcContainerHelper -ErrorAction Stop
                if (-not (Get-Command Check-BcContainerHelperPermissions -ErrorAction SilentlyContinue)) {
                    Write-Host 'Cmdlet Check-BcContainerHelperPermissions nicht verfuegbar.'
                    Read-Host 'Enter zum Beenden'
                    exit 2
                }
                Check-BcContainerHelperPermissions -Fix
                Write-Host ''
                Write-Host 'Fertig. Eventuell ist eine Neuanmeldung noetig (docker-users-Group).'
            } catch {
                Write-Host ('Fehler: ' + $_.Exception.Message)
                Read-Host 'Enter zum Beenden'
                exit 1
            }
            Read-Host 'Enter zum Schliessen'
            exit 0
            """;
        try
        {
            await File.WriteAllTextAsync(tempScript, script, ct).ConfigureAwait(false);
            return await _elevation.RunElevatedAsync(
                fileName: "powershell.exe",
                arguments: $"-NoProfile -ExecutionPolicy Bypass -File \"{tempScript}\"",
                timeout: TimeSpan.FromMinutes(3),
                cancellationToken: ct).ConfigureAwait(false);
        }
        finally
        {
            try { File.Delete(tempScript); } catch { /* nicht kritisch */ }
        }
    }

    private async Task<bool> InstallDockerDesktopElevatedAsync(CancellationToken ct)
    {
        // Skript in temp ablegen, damit der elevated PowerShell-Process es per
        // -File aufrufen kann — schöneres Logging als ein zusammengeklebter -Command.
        var tempScript = Path.Combine(Path.GetTempPath(), $"bccl-docker-install-{Guid.NewGuid():N}.ps1");
        const string script = """
            $ErrorActionPreference = 'Stop'
            Write-Host '== BC Container Creator: Docker Desktop Setup =='

            $winget = Get-Command winget -ErrorAction SilentlyContinue
            if (-not $winget) {
                Write-Host 'winget nicht gefunden. Bitte App Installer aus dem Microsoft Store installieren oder Docker Desktop manuell:'
                Write-Host '  https://www.docker.com/products/docker-desktop/'
                Start-Process 'https://www.docker.com/products/docker-desktop/'
                Read-Host 'Druecke Enter zum Beenden'
                exit 2
            }

            Write-Host 'Starte: winget install Docker.DockerDesktop ...'
            & winget install --exact --id Docker.DockerDesktop `
                --silent `
                --accept-package-agreements `
                --accept-source-agreements `
                --scope machine
            $code = $LASTEXITCODE
            Write-Host ("winget exit code: {0}" -f $code)
            if ($code -ne 0 -and $code -ne -1978335189) {
                # -1978335189 = APPINSTALLER_CLI_ERROR_PACKAGE_ALREADY_INSTALLED
                Write-Host 'Install fehlgeschlagen. Fenster bleibt offen — bitte Output kopieren.'
                Read-Host 'Enter zum Beenden'
                exit $code
            }

            Write-Host ''
            Write-Host 'Docker Desktop wurde installiert oder ist bereits vorhanden.'
            Write-Host 'Beim ersten Start wird Docker Desktop ggf. WSL2-Komponenten nachladen und einen Neustart verlangen.'
            Write-Host ''
            Read-Host 'Enter zum Schliessen'
            exit 0
            """;

        try
        {
            await File.WriteAllTextAsync(tempScript, script, ct).ConfigureAwait(false);
            return await _elevation.RunElevatedAsync(
                fileName: "powershell.exe",
                arguments: $"-NoProfile -ExecutionPolicy Bypass -File \"{tempScript}\"",
                timeout: TimeSpan.FromMinutes(20),
                cancellationToken: ct).ConfigureAwait(false);
        }
        finally
        {
            try { File.Delete(tempScript); } catch { /* nicht kritisch */ }
        }
    }

    private static string GetFixScript(string fixId)
    {
        var bcch = Constants.BcContainerHelperModule;
        var legacy = Constants.LegacyNavContainerHelperModule;

        return fixId switch
        {
            // Group-Policy oder LocalMachine-Override kann CurrentUser-Set verhindern
            // (Security error). Wir versuchen CurrentUser, fallen still auf Process
            // zurück — was für unsere Zwecke (PSResource-Installs) ausreicht.
            "set-execution-policy" => """
                $changed = $false
                try {
                    Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned -Force -ErrorAction Stop
                    Write-Information 'ExecutionPolicy CurrentUser=RemoteSigned'
                    $changed = $true
                } catch {
                    Write-Warning ("CurrentUser-Set fehlgeschlagen ({0}). Versuche Scope=Process." -f $_.Exception.Message)
                }
                if (-not $changed) {
                    Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force -ErrorAction Stop
                    Write-Information 'ExecutionPolicy Process=Bypass'
                }
                """,

            // PowerShellGet 1.0.0.1 (vom Windows-PowerShell-5.1-Pfad) ist mit
            // PS7-In-Process-SDK inkompatibel und wirft '$script:IsWindows' /
            // '$LocalizedData' Fehler. Wir bootstrappen daher PSResourceGet
            // direkt aus dem PSGallery-NuGet-Feed und benutzen Install-PSResource.
            "install-nuget-provider" =>
                EnsurePSResourceGetScript() +
                "\nWrite-Information 'PackageManagement/NuGet-Provider werden über PSResourceGet ersetzt.'",

            "trust-psgallery" =>
                EnsurePSResourceGetScript() +
                "\nSet-PSResourceRepository -Name PSGallery -Trusted -ErrorAction Stop",

            "install-bccontainerhelper" =>
                EnsurePSResourceGetScript() +
                $"\nInstall-PSResource -Name {bcch} -Repository PSGallery -TrustRepository -Reinstall -Scope CurrentUser -ErrorAction Stop",

            "remove-legacy-module" =>
                $"Get-Module -Name {legacy} | Remove-Module -Force -ErrorAction SilentlyContinue\n" +
                EnsurePSResourceGetScript() +
                $"\n$installed = Get-InstalledPSResource -Name {legacy} -ErrorAction SilentlyContinue\n" +
                $"if ($installed) {{ Uninstall-PSResource -Name {legacy} -Scope CurrentUser -ErrorAction SilentlyContinue }}",

            _ => throw new ArgumentException($"Unbekannte Fix-ID: {fixId}", nameof(fixId))
        };
    }

    /// <summary>
    /// Liefert ein PowerShell-Skript, das nach Ausführung garantiert
    /// <c>Install-PSResource</c> bzw. <c>Set-PSResourceRepository</c> verfügbar
    /// macht. Falls nicht installiert, wird Microsoft.PowerShell.PSResourceGet
    /// als nupkg aus PSGallery heruntergeladen, in den User-Module-Pfad
    /// entpackt und importiert. Das umgeht den PowerShellGet-1.0.0.1-Bug
    /// unter PS7-In-Process komplett.
    /// </summary>
    private static string EnsurePSResourceGetScript() => """
        $InformationPreference = 'Continue'

        if (Get-Command Install-PSResource -ErrorAction SilentlyContinue) {
            Write-Information 'PSResourceGet bereits geladen.'
        } else {
            Write-Information 'Suche installierte Microsoft.PowerShell.PSResourceGet ...'
            $existing = Get-Module -ListAvailable -Name Microsoft.PowerShell.PSResourceGet -ErrorAction SilentlyContinue |
                        Sort-Object Version -Descending | Select-Object -First 1
            if ($existing) {
                Write-Information ("Import {0} v{1}" -f $existing.Name, $existing.Version)
                Import-Module $existing -Force -ErrorAction Stop
            }
        }

        if (-not (Get-Command Install-PSResource -ErrorAction SilentlyContinue)) {
            Write-Information 'Bootstrap-Download Microsoft.PowerShell.PSResourceGet aus PSGallery startet ...'
            $version  = '1.1.1'
            $tmpDir   = Join-Path $env:TEMP "bccl-psresget-$([Guid]::NewGuid().ToString('N'))"
            New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null

            try {
                # TLS 1.2 erzwingen (manche Hosts haben Default 1.0/1.1).
                try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 } catch {}

                $nupkg = Join-Path $tmpDir 'psresourceget.nupkg'
                $url   = "https://www.powershellgallery.com/api/v2/package/Microsoft.PowerShell.PSResourceGet/$version"
                $oldProgress = $ProgressPreference
                $ProgressPreference = 'SilentlyContinue'
                Write-Information "Download $url ..."
                try {
                    Invoke-WebRequest -Uri $url -OutFile $nupkg -UseBasicParsing -TimeoutSec 60 -ErrorAction Stop
                } finally {
                    $ProgressPreference = $oldProgress
                }
                $size = (Get-Item $nupkg).Length
                Write-Information ("Download fertig: {0:N0} bytes" -f $size)

                Add-Type -AssemblyName System.IO.Compression.FileSystem
                $extractDir = Join-Path $tmpDir 'extracted'
                [System.IO.Compression.ZipFile]::ExtractToDirectory($nupkg, $extractDir)
                Write-Information 'Entpackt.'

                $userModuleRoot = Join-Path $env:USERPROFILE "Documents\PowerShell\Modules\Microsoft.PowerShell.PSResourceGet\$version"
                if (Test-Path $userModuleRoot) {
                    Remove-Item $userModuleRoot -Recurse -Force -ErrorAction SilentlyContinue
                }
                New-Item -ItemType Directory -Force -Path $userModuleRoot | Out-Null

                Get-ChildItem -Path $extractDir -Force |
                    Where-Object { $_.Name -notin @('_rels','package','[Content_Types].xml') -and $_.Extension -ne '.nuspec' } |
                    ForEach-Object { Copy-Item -Path $_.FullName -Destination $userModuleRoot -Recurse -Force }

                $psd1 = Join-Path $userModuleRoot 'Microsoft.PowerShell.PSResourceGet.psd1'
                if (-not (Test-Path $psd1)) {
                    throw "Bootstrap fehlgeschlagen: $psd1 nicht gefunden."
                }

                Import-Module $psd1 -Force -ErrorAction Stop
                Write-Information 'PSResourceGet importiert.'
            } finally {
                Remove-Item $tmpDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        if (-not (Get-Command Install-PSResource -ErrorAction SilentlyContinue)) {
            throw 'Bootstrap von Microsoft.PowerShell.PSResourceGet fehlgeschlagen.'
        }
        """;
}
