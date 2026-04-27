using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace BcContainerCreator.Core.PowerShell;

/// <summary>
/// Externer PowerShell-Runner. Führt jedes Skript in einem frischen
/// <c>powershell.exe</c>-Prozess (Windows PowerShell 5.1) aus statt im
/// In-Process-SDK-Runspace — letzteres lieferte unter BcContainerHelper
/// "The type initializer for 'AddTypeCommand' threw an exception", weil
/// die SDK-Embed-Engine an einigen .NET-Reflection-Stellen anders tickt
/// als eine echte powershell.exe.
/// <para>
/// stdout / stderr werden zeilenweise asynchron gelesen, in <see cref="ILogger"/>
/// und über <see cref="OutputReceived"/> an die UI gestreamt. Variablen werden
/// als ACL-geschützte JSON-Datei (User-Temp) übergeben — nie via Prozessargument
/// und nie als Plain-Text in Logs.
/// </para>
/// </summary>
public sealed class PowerShellRunner : IPowerShellRunner
{
    private const string PowerShellExe = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";

    private readonly ILogger<PowerShellRunner> _logger;

    public event EventHandler<PowerShellOutputEventArgs>? OutputReceived;

    public PowerShellRunner(ILogger<PowerShellRunner> logger)
    {
        _logger = logger;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task<PSResult> ExecuteAsync(
        string script,
        IDictionary<string, object?>? variables = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);

        var stopwatch = Stopwatch.StartNew();
        var stdoutLines = new List<string>();
        var stderrLines = new List<string>();
        var wasCancelled = false;

        string? paramFilePath = null;
        string? scriptFilePath = null;

        try
        {
            if (variables is { Count: > 0 })
            {
                paramFilePath = WriteParamFile(variables);
            }
            scriptFilePath = WriteScriptFile(script);

            using var process = new Process();
            process.StartInfo.FileName = PowerShellExe;
            process.StartInfo.Arguments =
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass " +
                $"-File \"{scriptFilePath}\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            process.StartInfo.StandardErrorEncoding = Encoding.UTF8;

            // Param-Pfad NICHT als Argument — via Environment-Variable, damit der
            // Pfad nicht im Task-Manager / Event-Logs sichtbar wird.
            if (paramFilePath is not null)
            {
                process.StartInfo.EnvironmentVariables["BCC_PARAM_FILE"] = paramFilePath;
            }

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                stdoutLines.Add(e.Data);
                _logger.LogInformation("PS[stdout] {Line}", e.Data);
                RaiseOutput(PSStreamType.Information, e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                stderrLines.Add(e.Data);
                _logger.LogError("PS[stderr] {Line}", e.Data);
                RaiseOutput(PSStreamType.Error, e.Data);
            };

            if (!process.Start())
            {
                throw new InvalidOperationException("powershell.exe konnte nicht gestartet werden.");
            }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var reg = cancellationToken.Register(() =>
            {
                wasCancelled = true;
                try
                {
                    if (!process.HasExited)
                    {
                        // entireProcessTree:true — auch Sub-Prozesse von BcContainerHelper.
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Process.Kill bei Cancellation fehlgeschlagen");
                }
            });

            try
            {
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WaitForExitAsync fehlgeschlagen");
            }

            // Stream-Reader async-flushen.
            process.WaitForExit();

            stopwatch.Stop();
            var exitCode = process.ExitCode;
            var success = !wasCancelled && exitCode == 0;

            // stdout-Zeilen als PSObject-Wrapper, damit Aufrufer mit
            // Objects.FirstOrDefault()?.ToString() weiter funktionieren.
            var objects = stdoutLines.Select(l => new PSObject(l)).ToList();

            return new PSResult(
                Success: success,
                Objects: objects,
                Errors: stderrLines.ToList(),
                Duration: stopwatch.Elapsed,
                WasCancelled: wasCancelled,
                ExitCode: exitCode);
        }
        catch (Exception ex) when (!wasCancelled)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "PowerShell-Ausführung fehlgeschlagen");
            stderrLines.Add(ex.Message);
            return new PSResult(false, Array.Empty<PSObject>(), stderrLines, stopwatch.Elapsed, wasCancelled, -1);
        }
        finally
        {
            // Param-File ZUERST löschen (enthält ggf. Klartext-Passwort).
            if (paramFilePath is not null)
            {
                TryDelete(paramFilePath);
            }
            if (scriptFilePath is not null)
            {
                TryDelete(scriptFilePath);
            }
        }
    }

    private string WriteScriptFile(string userScript)
    {
        var path = Path.Combine(Path.GetTempPath(), $"bcc-{Guid.NewGuid():N}.ps1");

        var prefix = """
            $ErrorActionPreference = 'Stop'
            $InformationPreference = 'Continue'
            $WarningPreference = 'Continue'
            $VerbosePreference = 'Continue'
            $ProgressPreference = 'SilentlyContinue'
            try { [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false) } catch {}

            $Params = $null
            if ($env:BCC_PARAM_FILE -and (Test-Path -LiteralPath $env:BCC_PARAM_FILE)) {
                try {
                    $Params = Get-Content -LiteralPath $env:BCC_PARAM_FILE -Raw | ConvertFrom-Json
                } catch {
                    Write-Warning "Konnte Parameter-Datei nicht laden: $($_.Exception.Message)"
                }
            }

            try {
            """;

        var suffix = """
            } catch {
                $err = $_
                Write-Host '--- FEHLER ---'
                Write-Host ("  Type:    {0}" -f $err.Exception.GetType().FullName)
                Write-Host ("  Message: {0}" -f $err.Exception.Message)
                $inner = $err.Exception.InnerException
                while ($inner) {
                    Write-Host ("  Inner:   {0}: {1}" -f $inner.GetType().FullName, $inner.Message)
                    $inner = $inner.InnerException
                }
                if ($err.ScriptStackTrace) {
                    Write-Host ("  Stack:   {0}" -f $err.ScriptStackTrace)
                }
                if ($err.InvocationInfo -and $err.InvocationInfo.PositionMessage) {
                    Write-Host ("  Pos:     {0}" -f $err.InvocationInfo.PositionMessage)
                }
                # Auch in stderr — landet so in PSResult.Errors.
                [Console]::Error.WriteLine($err.Exception.Message)
                exit 1
            }
            exit 0
            """;

        var wrapped = prefix + Environment.NewLine + userScript + Environment.NewLine + suffix;
        File.WriteAllText(path, wrapped, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private string WriteParamFile(IDictionary<string, object?> variables)
    {
        var path = Path.Combine(Path.GetTempPath(), $"bcc-params-{Guid.NewGuid():N}.json");

        var serializable = new Dictionary<string, object?>(StringComparer.Ordinal);
        var sensitiveKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in variables)
        {
            if (kvp.Value is SecureString ss)
            {
                serializable[kvp.Key] = SecureStringToPlain(ss);
                sensitiveKeys.Add(kvp.Key);
            }
            else
            {
                serializable[kvp.Key] = kvp.Value;
            }
        }

        var json = JsonSerializer.Serialize(serializable);
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        TryRestrictAclToCurrentUser(path);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var summary = string.Join(", ", variables.Keys.Select(k =>
                sensitiveKeys.Contains(k) ? $"{k}=<redacted>" : $"{k}={Truncate(variables[k]?.ToString())}"));
            _logger.LogDebug("PS-Parameter geschrieben: {Summary}", summary);
        }

        return path;
    }

    private void TryRestrictAclToCurrentUser(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            var acl = fi.GetAccessControl();
            acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            var rules = acl.GetAccessRules(true, false, typeof(SecurityIdentifier));
            foreach (FileSystemAccessRule rule in rules)
            {
                acl.RemoveAccessRule(rule);
            }

            using var identity = WindowsIdentity.GetCurrent();
            if (identity.User is not null)
            {
                acl.AddAccessRule(new FileSystemAccessRule(
                    identity.User,
                    FileSystemRights.FullControl,
                    AccessControlType.Allow));
            }

            fi.SetAccessControl(acl);
        }
        catch (Exception ex)
        {
            // User-Temp ist per default User-only, daher nicht fatal.
            _logger.LogWarning(ex, "ACL-Restriktion auf Param-File fehlgeschlagen — fällt auf NTFS-Default zurück");
        }
    }

    private static string SecureStringToPlain(SecureString ss)
    {
        if (ss.Length == 0) return string.Empty;
        IntPtr bstr = IntPtr.Zero;
        try
        {
            bstr = Marshal.SecureStringToBSTR(ss);
            return Marshal.PtrToStringBSTR(bstr) ?? string.Empty;
        }
        finally
        {
            if (bstr != IntPtr.Zero)
            {
                Marshal.ZeroFreeBSTR(bstr);
            }
        }
    }

    private static string? Truncate(string? value) =>
        value is null ? null
        : value.Length <= 60 ? value
        : value[..57] + "...";

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Konnte Temp-Datei nicht löschen: {Path}", path);
        }
    }

    private void RaiseOutput(PSStreamType type, string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        try
        {
            OutputReceived?.Invoke(this, new PowerShellOutputEventArgs(type, message));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OutputReceived-Subscriber hat geworfen");
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
