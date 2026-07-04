using System.Collections.Concurrent;
using BcContainerCreator.Core.PowerShell;

namespace BcContainerCreator.Core.Tests.PowerShell;

/// <summary>
/// Test-Doppelgänger für IPowerShellRunner. Speichert alle Aufrufe und liefert
/// vorprogrammierte Antworten, damit Services ohne echten Runspace getestet werden.
/// </summary>
public sealed class FakePowerShellRunner : IPowerShellRunner
{
    private readonly ConcurrentDictionary<string, Func<PSResult>> _responders = new();
    public List<(string Script, IDictionary<string, object?>? Variables)> Calls { get; } = new();

    public event EventHandler<PowerShellOutputEventArgs>? OutputReceived;

    /// <summary>
    /// Setzt für ein Skript-Substring eine Antwort. Bei mehreren passenden
    /// Patterns gewinnt deterministisch das LÄNGSTE konfigurierte Substring
    /// (Longest-Match) — die Iterations-Reihenfolge eines
    /// <see cref="ConcurrentDictionary{TKey,TValue}"/> wäre undefiniert.
    /// </summary>
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

        var responder = _responders
            .Where(kvp => script.Contains(kvp.Key, StringComparison.Ordinal))
            .OrderByDescending(kvp => kvp.Key.Length)
            .Select(kvp => kvp.Value)
            .FirstOrDefault();
        if (responder is not null)
        {
            return Task.FromResult(responder());
        }

        // Default: leeres Erfolgs-Resultat.
        return Task.FromResult(new PSResult(true, Array.Empty<string>(), Array.Empty<string>(), TimeSpan.Zero));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void RaiseOutput(PSStreamType type, string message) =>
        OutputReceived?.Invoke(this, new PowerShellOutputEventArgs(type, message));

    public static PSResult Success(params string[] outputs) =>
        new(true, outputs.ToList(), Array.Empty<string>(), TimeSpan.Zero);

    public static PSResult Failure(string error) =>
        new(false, Array.Empty<string>(), new[] { error }, TimeSpan.Zero);
}
