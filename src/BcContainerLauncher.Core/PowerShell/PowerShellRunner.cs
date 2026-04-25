using System.Diagnostics;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using Microsoft.Extensions.Logging;

namespace BcContainerLauncher.Core.PowerShell;

/// <summary>
/// PowerShell-Runner mit persistentem Runspace. Threadsicher (serialisiert
/// Aufrufe über ein <see cref="SemaphoreSlim"/>, weil ein Runspace nicht
/// parallel von mehreren Pipelines benutzt werden darf).
/// </summary>
public sealed class PowerShellRunner : IPowerShellRunner
{
    private readonly ILogger<PowerShellRunner> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Runspace? _runspace;
    private bool _disposed;

    public event EventHandler<PowerShellOutputEventArgs>? OutputReceived;

    public PowerShellRunner(ILogger<PowerShellRunner> logger)
    {
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_runspace is { RunspaceStateInfo.State: RunspaceState.Opened })
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_runspace is { RunspaceStateInfo.State: RunspaceState.Opened })
            {
                return;
            }

            _logger.LogInformation("Initialisiere PowerShell-Runspace");

            // Microsoft.PowerShell.SDK liefert die Core-Module als loose Files
            // unter <exe>/runtimes/win/lib/net10.0/Modules — ohne expliziten
            // Eintrag im PSModulePath findet der Engine sie nicht und alle
            // Microsoft.PowerShell.{Security,Utility,Management}-Cmdlets fallen
            // (inkl. Set-ExecutionPolicy, Sort-Object, Join-Path, Invoke-WebRequest).
            // Muss VOR Runspace-Open gesetzt werden, weil Set-Item / Set-Variable
            // selbst aus Microsoft.PowerShell.Management kommen würden (Henne-Ei).
            EnsureSdkModulesOnProcessPath();

            // CreateDefault2 ist deutlich schlanker (lädt nur Microsoft.PowerShell.Core)
            // und damit ~10x schneller im Open() als CreateDefault.
            var iss = InitialSessionState.CreateDefault2();
            // ExecutionPolicy für diesen Runspace; greift nicht systemweit.
            iss.ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy.Bypass;

            var rs = RunspaceFactory.CreateRunspace(iss);
            rs.Open();
            _runspace = rs;

            _logger.LogInformation("PowerShell-Runspace bereit (PSVersion: {Version})", rs.Version);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PSResult> ExecuteAsync(
        string script,
        IDictionary<string, object?>? variables = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();
        var wasCancelled = false;

        try
        {
            using var ps = System.Management.Automation.PowerShell.Create();
            ps.Runspace = _runspace!;

            // Variablen setzen, bevor das Skript läuft.
            if (variables is not null)
            {
                foreach (var kvp in variables)
                {
                    _runspace!.SessionStateProxy.SetVariable(kvp.Key, kvp.Value);
                }
            }

            // Stream-Subscriptions: Information, Warning, Error, Verbose, Debug, Progress.
            ps.Streams.Information.DataAdded += (_, e) =>
                OnOutput(PSStreamType.Information, ps.Streams.Information[e.Index]?.MessageData?.ToString() ?? string.Empty);
            ps.Streams.Warning.DataAdded += (_, e) =>
                OnOutput(PSStreamType.Warning, ps.Streams.Warning[e.Index]?.Message ?? string.Empty);
            ps.Streams.Error.DataAdded += (_, e) =>
            {
                var rec = ps.Streams.Error[e.Index];
                var msg = FormatError(rec);
                errors.Add(msg);
                OnOutput(PSStreamType.Error, msg);
            };
            ps.Streams.Verbose.DataAdded += (_, e) =>
                OnOutput(PSStreamType.Verbose, ps.Streams.Verbose[e.Index]?.Message ?? string.Empty);
            ps.Streams.Debug.DataAdded += (_, e) =>
                OnOutput(PSStreamType.Debug, ps.Streams.Debug[e.Index]?.Message ?? string.Empty);
            ps.Streams.Progress.DataAdded += (_, e) =>
            {
                var p = ps.Streams.Progress[e.Index];
                OnOutput(PSStreamType.Progress, $"{p.Activity}: {p.StatusDescription} ({p.PercentComplete}%)");
            };

            ps.AddScript(script);

            // Cancellation registrieren — BeginStop ist non-blocking.
            using var reg = cancellationToken.Register(() =>
            {
                wasCancelled = true;
                try { ps.BeginStop(null, null); }
                catch (Exception ex) { _logger.LogWarning(ex, "BeginStop fehlgeschlagen"); }
            });

            var input = new PSDataCollection<PSObject>();
            input.Complete();
            var output = new PSDataCollection<PSObject>();

            // Async-Ausführung über Begin/End-Pattern, in Task gewrappt.
            var psTask = Task.Factory.FromAsync(
                ps.BeginInvoke(input, output),
                ps.EndInvoke);

            try
            {
                await psTask.ConfigureAwait(false);
            }
            catch (PipelineStoppedException) when (wasCancelled)
            {
                // Vom CancellationToken erwartet.
            }

            stopwatch.Stop();
            var success = !wasCancelled && errors.Count == 0;
            return new PSResult(
                Success: success,
                Objects: output.ToList(),
                Errors: errors,
                Duration: stopwatch.Elapsed,
                WasCancelled: wasCancelled);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "PowerShell-Ausführung fehlgeschlagen");
            errors.Add(ex.Message);
            return new PSResult(false, Array.Empty<PSObject>(), errors, stopwatch.Elapsed, wasCancelled);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Hängt den SDK-eigenen Modules-Pfad ganz vorne in <c>$env:PSModulePath</c>
    /// des Runspace ein. Sucht relativ zur .exe nach <c>runtimes/win/lib/netX.0/Modules</c>
    /// — bei Single-File-Publish landet das im publish-Ordner als loose Files.
    /// </summary>
    private void EnsureSdkModulesOnProcessPath()
    {
        try
        {
            // Mehrere Kandidaten — abhängig davon, wie die App gestartet wurde:
            //   * Direkt-Start der .exe → Environment.ProcessPath ist korrekt
            //   * Via 'dotnet xxx.dll'  → ProcessPath zeigt auf dotnet.exe;
            //     AppContext.BaseDirectory zeigt aufs DLL-Verzeichnis
            //   * Single-File self-extract → AppContext.BaseDirectory ist Extract-Pfad
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(Environment.ProcessPath))
            {
                candidates.Add(Path.GetDirectoryName(Environment.ProcessPath)!);
            }
            candidates.Add(AppContext.BaseDirectory);
            var asmLoc = typeof(PowerShellRunner).Assembly.Location;
            if (!string.IsNullOrEmpty(asmLoc))
            {
                candidates.Add(Path.GetDirectoryName(asmLoc)!);
            }

            string? sdkModules = null;
            string? checkedRoot = null;
            foreach (var dir in candidates.Distinct())
            {
                checkedRoot = dir;
                var runtimesDir = Path.Combine(dir, "runtimes", "win", "lib");
                if (!Directory.Exists(runtimesDir))
                {
                    continue;
                }

                sdkModules = Directory.EnumerateDirectories(runtimesDir, "net*")
                    .Select(d => Path.Combine(d, "Modules"))
                    .FirstOrDefault(Directory.Exists);

                if (sdkModules is not null)
                {
                    break;
                }
            }

            if (sdkModules is null)
            {
                _logger.LogWarning("SDK-Modules-Pfad nicht gefunden (zuletzt geprüft: {Path}) — Cmdlet-Auflösung fällt auf System-PowerShell zurück.", checkedRoot);
                return;
            }

            // Direkt am Process — kein PowerShell-Cmdlet, weil die fehlen ja gerade.
            var current = Environment.GetEnvironmentVariable("PSModulePath") ?? string.Empty;
            // Idempotent: nur prependen, wenn nicht schon drin.
            var segments = current.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(s => string.Equals(s.TrimEnd('\\', '/'), sdkModules.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogDebug("SDK-Modules-Pfad bereits im PSModulePath: {Path}", sdkModules);
                return;
            }

            var newPath = string.IsNullOrEmpty(current)
                ? sdkModules
                : $"{sdkModules}{Path.PathSeparator}{current}";
            Environment.SetEnvironmentVariable("PSModulePath", newPath);

            _logger.LogInformation("SDK-Modules-Pfad vorangestellt: {Path}", sdkModules);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EnsureSdkModulesOnProcessPath fehlgeschlagen");
        }
    }

    private void OnOutput(PSStreamType type, string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        // Auch nach Serilog spiegeln, damit man bei Hängern im Log sieht,
        // wo das Skript steht.
        var level = type switch
        {
            PSStreamType.Error => LogLevel.Error,
            PSStreamType.Warning => LogLevel.Warning,
            PSStreamType.Information => LogLevel.Information,
            PSStreamType.Verbose => LogLevel.Debug,
            PSStreamType.Debug => LogLevel.Debug,
            PSStreamType.Progress => LogLevel.Debug,
            _ => LogLevel.Trace
        };
        _logger.Log(level, "PS[{Stream}] {Message}", type, message);

        try
        {
            OutputReceived?.Invoke(this, new PowerShellOutputEventArgs(type, message));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OutputReceived-Subscriber hat geworfen");
        }
    }

    private static string FormatError(ErrorRecord record)
    {
        var sb = new StringBuilder();
        sb.Append(record.Exception?.Message ?? record.ToString());
        if (record.InvocationInfo is { } info && !string.IsNullOrWhiteSpace(info.PositionMessage))
        {
            sb.Append(' ');
            sb.Append(info.PositionMessage.Trim());
        }
        return sb.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        try
        {
            if (_runspace is not null)
            {
                _runspace.Close();
                _runspace.Dispose();
                _runspace = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fehler beim Schließen des Runspace");
        }
        _gate.Dispose();
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
