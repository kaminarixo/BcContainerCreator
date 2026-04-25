using System.Text;
using BcContainerLauncher.Core.Models;
using BcContainerLauncher.Core.PowerShell;
using Microsoft.Extensions.Logging;

namespace BcContainerLauncher.Core.Containers;

/// <summary>
/// Übersetzt einen <see cref="ContainerCreateRequest"/> in einen
/// <c>New-BcContainer</c>-Aufruf und führt diesen aus. Das Passwort wird als
/// <c>SecureString</c>-Variable in den Runspace injiziert — niemals als String
/// im Skript.
/// </summary>
public sealed class ContainerService : IContainerService
{
    private readonly IPowerShellRunner _runner;
    private readonly ILogger<ContainerService> _logger;

    public ContainerService(IPowerShellRunner runner, ILogger<ContainerService> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    public string BuildCreateScript(ContainerCreateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        var sb = new StringBuilder();
        sb.AppendLine($"Import-Module {Constants.BcContainerHelperModule} -Force -ErrorAction Stop");
        sb.AppendLine();

        // Artifact URL holen. WICHTIG: 'latest' / leer → -version weglassen,
        // sonst sucht Get-BcArtifactUrl nach einer Version namens 'latest' und
        // liefert nichts zurück → "You have to specify artifactUrl"-Fehler bei New-BcContainer.
        var typeArg = request.ArtifactType == ArtifactType.Sandbox ? "Sandbox" : "OnPrem";
        var versionTrimmed = request.Version?.Trim() ?? string.Empty;
        var useExplicitVersion = !string.IsNullOrEmpty(versionTrimmed)
            && !string.Equals(versionTrimmed, "latest", StringComparison.OrdinalIgnoreCase);

        if (useExplicitVersion)
        {
            sb.AppendLine($"$artifactUrl = Get-BcArtifactUrl -type {typeArg} -country {Quote(request.Country)} -version {Quote(versionTrimmed)} -select Latest");
        }
        else
        {
            sb.AppendLine($"$artifactUrl = Get-BcArtifactUrl -type {typeArg} -country {Quote(request.Country)} -select Latest");
        }
        sb.AppendLine($"if (-not $artifactUrl) {{ throw \"Kein Artifact gefunden für type={typeArg}, country={request.Country}, version={(useExplicitVersion ? versionTrimmed : "latest")}.\" }}");
        sb.AppendLine("Write-Information \"Artifact-URL: $artifactUrl\"");
        sb.AppendLine();

        // Credential aus injiziertem SecureString aufbauen.
        if (request.AuthType == AuthType.NavUserPassword)
        {
            sb.AppendLine($"$cred = New-Object System.Management.Automation.PSCredential({Quote(request.Username)}, $bcPassword)");
            sb.AppendLine();
        }

        // New-BcContainer-Aufruf zusammenbauen.
        sb.Append("New-BcContainer ");
        sb.Append($"-containerName {Quote(request.ContainerName)} ");
        sb.Append("-artifactUrl $artifactUrl ");
        sb.Append($"-auth {(request.AuthType == AuthType.Windows ? "Windows" : "NavUserPassword")} ");

        if (request.AuthType == AuthType.NavUserPassword)
        {
            sb.Append("-credential $cred ");
        }

        if (request.AcceptEula)
        {
            sb.Append("-accept_eula ");
        }

        if (request.IncludeAL)
        {
            sb.Append("-includeAL ");
        }

        if (request.IncludeTestToolkit)
        {
            sb.Append("-includeTestToolkit ");
        }

        if (!string.IsNullOrWhiteSpace(request.LicenseFilePath))
        {
            sb.Append($"-licenseFile {Quote(request.LicenseFilePath!)} ");
        }

        if (!string.IsNullOrWhiteSpace(request.MemoryLimit))
        {
            sb.Append($"-memoryLimit {Quote(request.MemoryLimit!)} ");
        }

        if (!string.IsNullOrWhiteSpace(request.Isolation))
        {
            sb.Append($"-isolation {Quote(request.Isolation!)} ");
        }

        sb.AppendLine("-updateHosts");

        return sb.ToString();
    }

    public async Task<PSResult> CreateContainerAsync(
        ContainerCreateRequest request,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        progress?.Report($"Bereite Container '{request.ContainerName}' vor…");
        _logger.LogInformation("Erstelle Container {Name} ({Type}/{Country}/{Version})",
            request.ContainerName, request.ArtifactType, request.Country, request.Version);

        var script = BuildCreateScript(request);
        var variables = new Dictionary<string, object?>();

        // Passwort nur als SecureString in den Runspace.
        if (request.AuthType == AuthType.NavUserPassword)
        {
            variables["bcPassword"] = request.Password;
        }

        progress?.Report("Starte New-BcContainer (kann mehrere Minuten dauern)…");
        var result = await _runner.ExecuteAsync(script, variables, cancellationToken).ConfigureAwait(false);

        if (result.WasCancelled)
        {
            progress?.Report("Abgebrochen.");
        }
        else if (result.Success)
        {
            progress?.Report($"Container '{request.ContainerName}' erstellt ({result.Duration:mm\\:ss}).");
        }
        else
        {
            progress?.Report($"Fehlgeschlagen: {string.Join("; ", result.Errors)}");
        }

        return result;
    }

    public async Task<IReadOnlyList<string>> GetAvailableVersionsAsync(
        ArtifactType type,
        string country,
        int top = 15,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(country);
        if (top <= 0)
        {
            return Array.Empty<string>();
        }

        var typeArg = type == ArtifactType.Sandbox ? "Sandbox" : "OnPrem";
        var script = $$"""
            Import-Module {{Constants.BcContainerHelperModule}} -ErrorAction Stop
            $urls = Get-BCArtifactUrl -type {{typeArg}} -country '{{country}}' -select All -ErrorAction Stop
            $versions = $urls |
                ForEach-Object {
                    if ($_ -match '/(\d+\.\d+\.\d+\.\d+)/[^/]+/?$') { $matches[1] }
                } |
                Sort-Object -Unique -Property { [Version]$_ } -Descending |
                Select-Object -First {{top}}
            $versions
            """;

        var result = await _runner.ExecuteAsync(script, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            _logger.LogWarning("GetAvailableVersionsAsync fehlgeschlagen: {Errors}", string.Join("; ", result.Errors));
            return Array.Empty<string>();
        }
        return result.Objects
            .Select(o => o?.ToString() ?? string.Empty)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();
    }

    public async Task<string?> ResolveLatestVersionAsync(
        ArtifactType type,
        string country,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(country);

        var typeArg = type == ArtifactType.Sandbox ? "Sandbox" : "OnPrem";
        var script =
            $"Import-Module {Constants.BcContainerHelperModule} -ErrorAction Stop\n" +
            $"$url = Get-BCArtifactUrl -type {typeArg} -country '{country}' -select Latest -ErrorAction SilentlyContinue\n" +
            "if ($url -and ($url -match '/(\\d+\\.\\d+\\.\\d+\\.\\d+)/[^/]+/?$')) { $matches[1] }";

        var result = await _runner.ExecuteAsync(script, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            _logger.LogWarning("ResolveLatestVersionAsync fehlgeschlagen: {Errors}", string.Join("; ", result.Errors));
            return null;
        }
        return result.Objects.FirstOrDefault()?.ToString();
    }

    private static void Validate(ContainerCreateRequest req)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(req.ContainerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(req.Country);
        ArgumentException.ThrowIfNullOrWhiteSpace(req.Version);

        if (req.AuthType == AuthType.NavUserPassword)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(req.Username);
            if (req.Password is null || req.Password.Length == 0)
            {
                throw new ArgumentException("Passwort ist erforderlich für NavUserPassword.", nameof(req));
            }
        }

        // Container-Name muss Docker-konform sein.
        foreach (var c in req.ContainerName)
        {
            if (!char.IsLetterOrDigit(c) && c is not ('-' or '_'))
            {
                throw new ArgumentException(
                    $"Ungültiges Zeichen '{c}' im Containername. Erlaubt: a-z, A-Z, 0-9, -, _.",
                    nameof(req));
            }
        }
    }

    /// <summary>
    /// PowerShell-Single-Quote-Escape: ' wird zu ''.
    /// </summary>
    private static string Quote(string value) => $"'{value.Replace("'", "''")}'";
}
