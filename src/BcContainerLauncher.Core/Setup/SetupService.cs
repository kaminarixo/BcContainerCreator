using BcContainerLauncher.Core.Docker;
using BcContainerLauncher.Core.PowerShell;
using Microsoft.Extensions.Logging;

namespace BcContainerLauncher.Core.Setup;

/// <summary>
/// Setzt einzelne Voraussetzungen instand. Jede Fix-Aktion ist idempotent
/// und sicher mehrfach ausführbar.
/// </summary>
public sealed class SetupService : ISetupService
{
    private readonly IPowerShellRunner _runner;
    private readonly IDockerService _docker;
    private readonly ILogger<SetupService> _logger;

    private static readonly Dictionary<string, string> Fixes = new()
    {
        ["set-execution-policy"] = "ExecutionPolicy auf RemoteSigned setzen",
        ["install-nuget-provider"] = "NuGet-PackageProvider installieren",
        ["trust-psgallery"] = "PSGallery als vertrauenswürdig markieren",
        ["install-bccontainerhelper"] = "BcContainerHelper aus PSGallery installieren",
        ["remove-legacy-module"] = "Legacy-Modul navcontainerhelper entfernen",
        ["switch-to-windows-mode"] = "Docker auf Windows-Container-Modus umschalten"
    };

    public SetupService(IPowerShellRunner runner, IDockerService docker, ILogger<SetupService> logger)
    {
        _runner = runner;
        _docker = docker;
        _logger = logger;
    }

    public IReadOnlyDictionary<string, string> AvailableFixes => Fixes;

    public async Task<bool> ApplyFixAsync(string fixId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixId);
        _logger.LogInformation("Fix {FixId} wird angewendet", fixId);

        if (fixId == "switch-to-windows-mode")
        {
            return await _docker.SwitchToWindowsModeAsync(cancellationToken).ConfigureAwait(false);
        }

        var script = GetFixScript(fixId);
        var result = await _runner.ExecuteAsync(script, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            _logger.LogError("Fix {FixId} fehlgeschlagen: {Errors}", fixId, string.Join("; ", result.Errors));
        }
        return result.Success;
    }

    private static string GetFixScript(string fixId)
    {
        var bcch = Constants.BcContainerHelperModule;
        var legacy = Constants.LegacyNavContainerHelperModule;

        return fixId switch
        {
            "set-execution-policy" =>
                "Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned -Force",

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
        if (-not (Get-Command Install-PSResource -ErrorAction SilentlyContinue)) {
            $existing = Get-Module -ListAvailable -Name Microsoft.PowerShell.PSResourceGet -ErrorAction SilentlyContinue |
                        Sort-Object Version -Descending | Select-Object -First 1
            if ($existing) {
                Import-Module $existing -Force -ErrorAction Stop
            }
        }

        if (-not (Get-Command Install-PSResource -ErrorAction SilentlyContinue)) {
            Write-Information 'Microsoft.PowerShell.PSResourceGet wird aus PSGallery nachinstalliert...'

            $version  = '1.1.1'
            $tmpDir   = Join-Path $env:TEMP "bccl-psresget-$([Guid]::NewGuid().ToString('N'))"
            New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null

            try {
                $nupkg = Join-Path $tmpDir 'psresourceget.nupkg'
                $url   = "https://www.powershellgallery.com/api/v2/package/Microsoft.PowerShell.PSResourceGet/$version"
                $oldProgress = $ProgressPreference
                $ProgressPreference = 'SilentlyContinue'
                try {
                    Invoke-WebRequest -Uri $url -OutFile $nupkg -UseBasicParsing -ErrorAction Stop
                } finally {
                    $ProgressPreference = $oldProgress
                }

                Add-Type -AssemblyName System.IO.Compression.FileSystem
                $extractDir = Join-Path $tmpDir 'extracted'
                [System.IO.Compression.ZipFile]::ExtractToDirectory($nupkg, $extractDir)

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
            } finally {
                Remove-Item $tmpDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        if (-not (Get-Command Install-PSResource -ErrorAction SilentlyContinue)) {
            throw 'Bootstrap von Microsoft.PowerShell.PSResourceGet fehlgeschlagen.'
        }
        """;
}
