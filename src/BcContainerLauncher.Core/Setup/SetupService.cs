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
        // PowerShell-Skripte als rohe Strings — keine C#-Interpolation, weil
        // PS-Klammern sonst als Interpolations-Holes interpretiert werden.
        var bcch = Constants.BcContainerHelperModule;
        var legacy = Constants.LegacyNavContainerHelperModule;

        return fixId switch
        {
            "set-execution-policy" =>
                "Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned -Force",

            "install-nuget-provider" =>
                "Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Force -Scope CurrentUser | Out-Null",

            "trust-psgallery" =>
                "Set-PSRepository -Name PSGallery -InstallationPolicy Trusted",

            "install-bccontainerhelper" =>
                $"if (Get-Module -ListAvailable -Name {bcch}) {{ Update-Module -Name {bcch} -Force -ErrorAction Stop }} " +
                $"else {{ Install-Module -Name {bcch} -Scope CurrentUser -Force -AllowClobber -ErrorAction Stop }}",

            "remove-legacy-module" =>
                $"Get-Module -Name {legacy} | Remove-Module -Force -ErrorAction SilentlyContinue; " +
                $"Uninstall-Module -Name {legacy} -AllVersions -Force -ErrorAction SilentlyContinue",

            _ => throw new ArgumentException($"Unbekannte Fix-ID: {fixId}", nameof(fixId))
        };
    }
}
