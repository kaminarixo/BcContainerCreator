using BcContainerCreator.Core.Models;
using BcContainerCreator.Core.PowerShell;
using Microsoft.Extensions.Logging;

namespace BcContainerCreator.Core.Docker;

/// <summary>
/// PowerShell-basierte Docker-Steuerung über den externen Runner.
/// Skripte werfen explizit bei non-0-Exit, damit der Wrapper sie als
/// PSResult.Success=false durchreicht.
/// </summary>
public sealed class DockerService : IDockerService
{
    private readonly IPowerShellRunner _runner;
    private readonly ILogger<DockerService> _logger;

    /// <summary>Erzeugt den Service mit Runner und Logger (DI).</summary>
    public DockerService(IPowerShellRunner runner, ILogger<DockerService> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default)
    {
        // Get-Command wirft nicht, liefert null → robust prüfbar.
        var result = await _runner.ExecuteAsync(
            "if (Get-Command docker -ErrorAction SilentlyContinue) { Write-Output 'yes' } else { Write-Output 'no' }",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            _logger.LogWarning("Docker-CLI-Check fehlgeschlagen: {Errors}", string.Join("; ", result.Errors));
            return false;
        }
        return result.Objects.Any(o => string.Equals(o, "yes", StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public async Task<bool> IsRunningAsync(CancellationToken cancellationToken = default)
    {
        // 'docker info' wirft non-zero, wenn der Daemon nicht erreichbar ist.
        // Wrapper-Skript wertet $LASTEXITCODE aus und exited entsprechend.
        const string script = """
            $null = docker info 2>&1
            if ($LASTEXITCODE -ne 0) { exit 1 }
            """;
        var result = await _runner.ExecuteAsync(script, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Success;
    }

    /// <inheritdoc />
    public async Task<ContainerMode> GetContainerModeAsync(CancellationToken cancellationToken = default)
    {
        // 'docker info -f' liefert OSType: 'windows' oder 'linux'. Eine Zeile stdout.
        const string script = """
            $os = docker info --format '{{.OSType}}' 2>$null
            if ($LASTEXITCODE -ne 0) { exit 1 }
            Write-Output $os
            """;
        var result = await _runner.ExecuteAsync(script, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Success || result.Objects.Count == 0)
        {
            return ContainerMode.Unknown;
        }

        var osType = result.Objects[0]?.Trim().ToLowerInvariant();
        return osType switch
        {
            "windows" => ContainerMode.Windows,
            "linux" => ContainerMode.Linux,
            _ => ContainerMode.Unknown
        };
    }

    /// <inheritdoc />
    public async Task<bool> SwitchToWindowsModeAsync(CancellationToken cancellationToken = default)
    {
        const string script = """
            $cli = 'C:\Program Files\Docker\Docker\DockerCli.exe'
            if (-not (Test-Path $cli)) { throw "DockerCli.exe nicht gefunden unter $cli" }
            & $cli -SwitchDaemon
            if ($LASTEXITCODE -ne 0) { throw "DockerCli -SwitchDaemon exit $LASTEXITCODE" }
            """;
        var result = await _runner.ExecuteAsync(script, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            _logger.LogError("SwitchToWindowsMode fehlgeschlagen: {Errors}", string.Join("; ", result.Errors));
        }
        return result.Success;
    }
}
