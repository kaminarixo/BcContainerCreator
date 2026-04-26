using BcContainerCreator.Core.Models;
using BcContainerCreator.Core.PowerShell;
using Microsoft.Extensions.Logging;

namespace BcContainerCreator.Core.Docker;

/// <summary>
/// PowerShell-basierte Docker-Steuerung. Vermeidet eine direkte
/// Process.Start-Abhängigkeit, damit alles über den geteilten
/// PowerShell-Runspace läuft (einheitliches Logging/Streaming).
/// </summary>
public sealed class DockerService : IDockerService
{
    private readonly IPowerShellRunner _runner;
    private readonly ILogger<DockerService> _logger;

    public DockerService(IPowerShellRunner runner, ILogger<DockerService> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    public async Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default)
    {
        // Get-Command wirft nicht, sondern liefert null → robust prüfbar.
        var result = await _runner.ExecuteAsync(
            "if (Get-Command docker -ErrorAction SilentlyContinue) { 'yes' } else { 'no' }",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            _logger.LogWarning("Docker-CLI-Check fehlgeschlagen: {Errors}", string.Join("; ", result.Errors));
            return false;
        }
        return result.Objects.Any(o => string.Equals(o?.ToString(), "yes", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> IsRunningAsync(CancellationToken cancellationToken = default)
    {
        // 'docker info' wirft non-zero, wenn der Daemon nicht erreichbar ist.
        var result = await _runner.ExecuteAsync(
            "$null = docker info 2>&1; $LASTEXITCODE",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            return false;
        }

        var exitCodeObj = result.Objects.LastOrDefault();
        if (exitCodeObj?.BaseObject is int code)
        {
            return code == 0;
        }
        return false;
    }

    public async Task<ContainerMode> GetContainerModeAsync(CancellationToken cancellationToken = default)
    {
        // 'docker info -f' liefert OSType: 'windows' oder 'linux'.
        var result = await _runner.ExecuteAsync(
            "(docker info --format '{{.OSType}}' 2>$null)",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!result.Success || result.Objects.Count == 0)
        {
            return ContainerMode.Unknown;
        }

        var osType = result.Objects[0]?.ToString()?.Trim().ToLowerInvariant();
        return osType switch
        {
            "windows" => ContainerMode.Windows,
            "linux" => ContainerMode.Linux,
            _ => ContainerMode.Unknown
        };
    }

    public async Task<bool> SwitchToWindowsModeAsync(CancellationToken cancellationToken = default)
    {
        // Standard-Pfad zu DockerCli.exe in Docker Desktop 4.x+.
        // Phase-1-Heuristik: Wenn nicht vorhanden, abbrechen — kein Auto-Install.
        const string script = """
            $cli = 'C:\Program Files\Docker\Docker\DockerCli.exe'
            if (-not (Test-Path $cli)) { throw "DockerCli.exe nicht gefunden unter $cli" }
            & $cli -SwitchDaemon
            $LASTEXITCODE
            """;

        var result = await _runner.ExecuteAsync(script, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            _logger.LogError("SwitchToWindowsMode fehlgeschlagen: {Errors}", string.Join("; ", result.Errors));
            return false;
        }

        var exitCode = result.Objects.LastOrDefault()?.BaseObject as int?;
        return exitCode == 0;
    }
}
