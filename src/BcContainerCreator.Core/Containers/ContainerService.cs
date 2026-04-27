using System.Text;
using BcContainerCreator.Core.Models;
using BcContainerCreator.Core.PowerShell;
using Microsoft.Extensions.Logging;

namespace BcContainerCreator.Core.Containers;

/// <summary>
/// Übersetzt einen <see cref="ContainerCreateRequest"/> in einen
/// <c>New-BcContainer</c>-Aufruf und führt diesen aus. Username/Passwort
/// werden über die JSON-Parameter-Datei des externen PowerShell-Runners
/// übergeben (siehe <see cref="PowerShellRunner"/>) und im Skript via
/// <c>$Params.Username</c> / <c>$Params.Password</c> gelesen — niemals
/// in das Skript interpoliert oder geloggt.
/// </summary>
public sealed class ContainerService : IContainerService
{
    private readonly IPowerShellRunner _runner;
    private readonly IContainerMetadataStore _metadata;
    private readonly ILogger<ContainerService> _logger;

    public ContainerService(IPowerShellRunner runner, IContainerMetadataStore metadata, ILogger<ContainerService> logger)
    {
        _runner = runner;
        _metadata = metadata;
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

        // Credential aus dem $Params-Block (extern via JSON-File). Keine
        // String-Interpolation von Username/Password ins Skript.
        if (request.AuthType == AuthType.NavUserPassword)
        {
            sb.AppendLine("if (-not $Params -or -not $Params.Password) { throw 'Parameter-Datei fehlt oder Password nicht gesetzt.' }");
            sb.AppendLine("$securePassword = ConvertTo-SecureString $Params.Password -AsPlainText -Force");
            sb.AppendLine("$cred = New-Object System.Management.Automation.PSCredential($Params.Username, $securePassword)");
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

        if (request.Multitenant)
        {
            sb.Append("-multitenant ");
        }

        sb.AppendLine("-updateHosts");
        sb.AppendLine("Write-Information \"Container '\" + " + Quote(request.ContainerName) + " + \"' wurde erstellt.\"");

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

        // Username + Password über JSON-Param-File. Password ist SecureString,
        // wird intern im Runner via DPAPI-freier Konvertierung in den Tempfile
        // geschrieben und nach Run sofort gelöscht.
        var variables = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Username"] = request.Username,
        };
        if (request.AuthType == AuthType.NavUserPassword)
        {
            variables["Password"] = request.Password;
        }

        // Live-Output an die UI weiterleiten (PS-Stream-Lines kommen über
        // OutputReceived rein; Container-Helper schreibt sehr viel via
        // Write-Information / Write-Host, das landet alles auf stdout).
        EventHandler<PowerShellOutputEventArgs> handler = (_, e) => progress?.Report(e.Message);
        _runner.OutputReceived += handler;

        progress?.Report("Starte New-BcContainer (kann mehrere Minuten dauern)…");
        PSResult result;
        try
        {
            result = await _runner.ExecuteAsync(script, variables, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _runner.OutputReceived -= handler;
        }

        if (result.WasCancelled)
        {
            progress?.Report("Abgebrochen.");
        }
        else if (result.Success)
        {
            progress?.Report($"Container '{request.ContainerName}' erstellt ({result.Duration:mm\\:ss}).");

            try
            {
                await _metadata.SaveAsync(
                    containerName: request.ContainerName,
                    createdAt: DateTimeOffset.UtcNow,
                    authType: request.AuthType,
                    username: request.Username,
                    password: request.AuthType == AuthType.NavUserPassword ? request.Password : null,
                    artifactType: request.ArtifactType,
                    country: request.Country,
                    versionSelector: request.Version,
                    resolvedBuild: null,
                    webClientUrl: $"http://{request.ContainerName}/BC?tenant=default",
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Konnte Container-Metadaten nicht speichern");
            }
        }
        else
        {
            progress?.Report($"Fehlgeschlagen (ExitCode {result.ExitCode}): {string.Join("; ", result.Errors)}");
        }

        return result;
    }

    public async Task<IReadOnlyList<ArtifactVersionOption>> GetVersionOptionsAsync(
        ArtifactType type,
        string country,
        int topMajors = 6,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(country);
        if (topMajors <= 0)
        {
            return Array.Empty<ArtifactVersionOption>();
        }

        var typeArg = type == ArtifactType.Sandbox ? "Sandbox" : "OnPrem";
        var script = $$"""
            Import-Module {{Constants.BcContainerHelperModule}} -ErrorAction Stop
            $urls = Get-BCArtifactUrl -type {{typeArg}} -country '{{country}}' -select All -ErrorAction Stop
            $versions = $urls |
                ForEach-Object {
                    if ($_ -match '/(\d+\.\d+\.\d+\.\d+)/[^/]+/?$') { $matches[1] }
                }
            $byMajor = $versions | Group-Object { ($_ -split '\.')[0] }
            $rows = $byMajor | ForEach-Object {
                $latest = $_.Group | Sort-Object -Property { [Version]$_ } -Descending | Select-Object -First 1
                "$($_.Name)|$latest"
            } | Sort-Object -Property { [int]($_ -split '\|')[0] } -Descending |
                Select-Object -First {{topMajors}}
            $rows | ForEach-Object { Write-Output $_ }
            """;

        var result = await _runner.ExecuteAsync(script, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            _logger.LogWarning("GetVersionOptionsAsync fehlgeschlagen: {Errors}", string.Join("; ", result.Errors));
            return Array.Empty<ArtifactVersionOption>();
        }

        var options = new List<ArtifactVersionOption>();
        string? newestBuild = null;
        foreach (var obj in result.Objects)
        {
            var raw = obj?.ToString();
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var parts = raw.Split('|', 2);
            if (parts.Length < 2) continue;
            var major = parts[0].Trim();
            var latest = parts[1].Trim();
            if (string.IsNullOrEmpty(major) || string.IsNullOrEmpty(latest)) continue;
            options.Add(new ArtifactVersionOption(Selector: major, LatestBuild: latest));
            newestBuild ??= latest;
        }

        var withLatest = new List<ArtifactVersionOption>(options.Count + 1)
        {
            new("latest", newestBuild)
        };
        withLatest.AddRange(options);
        return withLatest;
    }

    public async Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(CancellationToken cancellationToken = default)
    {
        // 'docker ps -a --format json' liefert pro Zeile EIN JSON-Objekt
        // (NDJSON-Style). Externer Runner sammelt diese in result.Objects.
        const string script = """
            $output = docker ps -a --no-trunc --format '{{json .}}' 2>$null
            if ($LASTEXITCODE -ne 0) { throw "docker ps fehlgeschlagen (exit $LASTEXITCODE)" }
            $output | ForEach-Object { Write-Output $_ }
            """;
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
            // Wrapper-Zeilen wie '--- FEHLER ---' überspringen.
            if (!line.TrimStart().StartsWith("{")) continue;
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

                var url = isBc && !string.IsNullOrWhiteSpace(name)
                    ? $"http://{name}/BC?tenant=default"
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
        var script = $"docker start {quoted}\nif ($LASTEXITCODE -ne 0) {{ throw \"docker start exit $LASTEXITCODE\" }}";
        var result = await _runner.ExecuteAsync(script, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Success;
    }

    public async Task<bool> StopContainerAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var quoted = QuoteForDocker(name);
        var script = $"docker stop {quoted}\nif ($LASTEXITCODE -ne 0) {{ throw \"docker stop exit $LASTEXITCODE\" }}";
        var result = await _runner.ExecuteAsync(script, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Success;
    }

    public async Task<bool> RemoveContainerAsync(string name, bool force = true, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var quoted = QuoteForDocker(name);
        var forceFlag = force ? "-f " : string.Empty;
        var script = $"docker rm {forceFlag}{quoted}\nif ($LASTEXITCODE -ne 0) {{ throw \"docker rm exit $LASTEXITCODE\" }}";
        var result = await _runner.ExecuteAsync(script, cancellationToken: cancellationToken).ConfigureAwait(false);
        var ok = result.Success;
        if (ok)
        {
            try { await _metadata.DeleteAsync(name, cancellationToken).ConfigureAwait(false); }
            catch { /* nicht kritisch */ }
        }
        return ok;
    }

    public async Task<string> GetContainerLogsAsync(string name, int tail = 1000, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (tail <= 0) tail = 1000;
        var quoted = QuoteForDocker(name);
        var script = $"docker logs --tail {tail} {quoted} 2>&1\nif ($LASTEXITCODE -ne 0) {{ throw \"docker logs exit $LASTEXITCODE\" }}";
        var result = await _runner.ExecuteAsync(script, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Success && result.Objects.Count == 0)
        {
            return string.Join(Environment.NewLine, result.Errors);
        }
        return string.Join(Environment.NewLine, result.Objects.Select(o => o?.ToString() ?? string.Empty));
    }

    private static string GetString(System.Text.Json.JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) ? v.GetString() ?? string.Empty : string.Empty;

    /// <summary>
    /// Quoted einen docker-Argument-String defensiv (nur a-z A-Z 0-9 _ - .).
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
