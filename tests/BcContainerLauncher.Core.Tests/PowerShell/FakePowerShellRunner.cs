using System.Collections.Concurrent;
using System.Management.Automation;
using BcContainerLauncher.Core.PowerShell;

namespace BcContainerLauncher.Core.Tests.PowerShell;

/// <summary>
/// Test-Doppelgänger für IPowerShellRunner. Speichert alle Aufrufe und liefert
/// vorprogrammierte Antworten, damit Services ohne echten Runspace getestet werden.
/// </summary>
public sealed class FakePowerShellRunner : IPowerShellRunner
{
    private readonly ConcurrentDictionary<string, Func<PSResult>> _responders = new();
    public List<(string Script, IDictionary<string, object?>? Variables)> Calls { get; } = new();

    public event EventHandler<PowerShellOutputEventArgs>? OutputReceived;

    /// <summary>Setzt für ein Skript-Substring eine Antwort.</summary>
    public FakePowerShellRunner WhenScriptContains(string substring, Func<PSResult> responder)
    {
        _responders[substring] = responder;
        return this;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<PSResult> ExecuteAsync(
        string script,
        IDictionary<string, object?>? variables = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add((script, variables));

        foreach (var (key, responder) in _responders)
        {
            if (script.Contains(key, StringComparison.Ordinal))
            {
                return Task.FromResult(responder());
            }
        }

        // Default: leeres Erfolgs-Resultat.
        return Task.FromResult(new PSResult(true, Array.Empty<PSObject>(), Array.Empty<string>(), TimeSpan.Zero));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void RaiseOutput(PSStreamType type, string message) =>
        OutputReceived?.Invoke(this, new PowerShellOutputEventArgs(type, message));

    public static PSResult Success(params string[] outputs)
    {
        var objs = outputs.Select(o => new PSObject(o)).ToList();
        return new PSResult(true, objs, Array.Empty<string>(), TimeSpan.Zero);
    }

    public static PSResult Failure(string error) =>
        new(false, Array.Empty<PSObject>(), new[] { error }, TimeSpan.Zero);
}
