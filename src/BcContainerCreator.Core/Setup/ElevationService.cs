using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace BcContainerCreator.Core.Setup;

/// <summary>
/// Startet Prozesse elevated über <c>Verb=runas</c> (UAC-Prompt). Ein vom
/// User abgebrochener UAC-Prompt gilt als reguläres "nicht ausgeführt"
/// (false), nicht als Fehler.
/// </summary>
public sealed class ElevationService : IElevationService
{
    private readonly ILogger<ElevationService> _logger;
    private readonly Func<ProcessStartInfo, Process?> _startProcess;

    /// <summary>
    /// Erzeugt den Service (DI). <paramref name="startProcess"/> ist ein
    /// Test-Hook analog zur Admin-Probe im SetupService — Default ist das
    /// echte <see cref="Process.Start(ProcessStartInfo)"/>.
    /// </summary>
    public ElevationService(
        ILogger<ElevationService> logger,
        Func<ProcessStartInfo, Process?>? startProcess = null)
    {
        _logger = logger;
        _startProcess = startProcess ?? Process.Start;
    }

    /// <inheritdoc />
    public async Task<bool> RunElevatedAsync(
        string fileName,
        string arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments ?? string.Empty,
            UseShellExecute = true,            // Pflicht, damit Verb=runas greift
            Verb = "runas",
            CreateNoWindow = false,
            WindowStyle = ProcessWindowStyle.Normal,
        };

        try
        {
            using var process = _startProcess(psi);
            if (process is null)
            {
                _logger.LogWarning("Process.Start lieferte null für {File}", fileName);
                return false;
            }

            _logger.LogInformation("Elevated Process gestartet: {File} {Args} (PID {Pid})",
                fileName, arguments, process.Id);

            var effectiveTimeout = timeout ?? TimeSpan.FromMinutes(5);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(effectiveTimeout);

            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);

            _logger.LogInformation("Elevated Process beendet (ExitCode {Code})", process.ExitCode);
            return process.ExitCode == 0;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED: User hat den UAC-Prompt verworfen.
            _logger.LogInformation("UAC-Prompt vom User abgebrochen");
            return false;
        }
        catch (Win32Exception ex)
        {
            _logger.LogError(ex, "RunElevatedAsync fehlgeschlagen (Win32 {Code})", ex.NativeErrorCode);
            return false;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Elevated Process gestoppt (Timeout/Cancel)");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RunElevatedAsync unerwarteter Fehler");
            return false;
        }
    }
}
