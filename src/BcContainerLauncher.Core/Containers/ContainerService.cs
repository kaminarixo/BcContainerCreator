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

    public async Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(CancellationToken cancellationToken = default)
    {
        // 'docker ps -a --format json' liefert pro Zeile EIN JSON-Objekt
        // (NDJSON-Style). Wir lesen die Zeilen ein und parsen jede für sich.
        const string script = @"docker ps -a --no-trunc --format '{{json .}}' 2>$null";
        var result = await _runner.ExecuteAsync(script, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            _logger.LogWarning("docker ps fehlgeschlagen: {Errors}", string.Join("; ", result.Errors));
            return Array.Empty<ContainerInfo>();
        }

        var containers = new List<ContainerInfo>();
        foreach (var obj in result.Objects)
        {
            var line = obj?.ToString();
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(line);
                var root = doc.RootElement;
                var name = GetString(root, "Names");
                var image = GetString(root, "Image");
                var status = GetString(root, "Status");
                var id = GetString(root, "ID");
                var ports = GetString(root, "Ports");
                var state = GetString(root, "State");

                var isRunning = string.Equals(state, "running", StringComparison.OrdinalIgnoreCase)
                    || status.StartsWith("Up ", StringComparison.OrdinalIgnoreCase);

                var isBc = image.Contains("bcartifacts", StringComparison.OrdinalIgnoreCase)
                    || image.Contains("businesscentral", StringComparison.OrdinalIgnoreCase)
                    || image.Contains("bcsandbox", StringComparison.OrdinalIgnoreCase);

                // BcContainerHelper trägt typischerweise einen Hostfile-Eintrag
                // <name> -> Container-IP ein, sodass http://<name>/BC erreichbar ist.
                var url = isBc && !string.IsNullOrWhiteSpace(name)
                    ? $"http://{name}/BC"
                    : null;

                containers.Add(new ContainerInfo(id, name, image, status, isRunning, ports, url, isBc));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Konnte docker-ps-Zeile nicht parsen: {Line}", line);
            }
        }
        return containers;
    }

    public async Task<bool> StartContainerAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var quoted = QuoteForDocker(name);
        var result = await _runner.ExecuteAsync($"docker start {quoted}; $LASTEXITCODE", cancellationToken: cancellationToken).ConfigureAwait(false);
        return WasZeroExit(result);
    }

    public async Task<bool> StopContainerAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var quoted = QuoteForDocker(name);
        var result = await _runner.ExecuteAsync($"docker stop {quoted}; $LASTEXITCODE", cancellationToken: cancellationToken).ConfigureAwait(false);
        return WasZeroExit(result);
    }

    public async Task<bool> RemoveContainerAsync(string name, bool force = true, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var quoted = QuoteForDocker(name);
        var forceFlag = force ? "-f " : string.Empty;
        var result = await _runner.ExecuteAsync($"docker rm {forceFlag}{quoted}; $LASTEXITCODE", cancellationToken: cancellationToken).ConfigureAwait(false);
        return WasZeroExit(result);
    }

    private static bool WasZeroExit(PSResult result)
    {
        if (!result.Success) return false;
        var exit = result.Objects.LastOrDefault()?.BaseObject as int?;
        return exit == 0;
    }

    private static string GetString(System.Text.Json.JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) ? v.GetString() ?? string.Empty : string.Empty;

    /// <summary>
    /// Quoted einen docker-Argument-String defensiv (nur a-z A-Z 0-9 _ -)
    /// — Container-Namen sind genau aus diesem Alphabet, daher Pass-through.
    /// </summary>
    private static string QuoteForDocker(string s)
    {
        foreach (var c in s)
        {
            if (!char.IsLetterOrDigit(c) && c is not ('-' or '_' or '.'))
            {
                throw new ArgumentException($"Ungültiger Container-Name (enthält '{c}').", nameof(s));
            }
        }
        return s;
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
