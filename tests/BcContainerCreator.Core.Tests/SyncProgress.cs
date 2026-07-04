namespace BcContainerCreator.Core.Tests;

/// <summary>
/// Synchroner <see cref="IProgress{T}"/>-Ersatz für Tests.
/// <see cref="Progress{T}"/> postet Reports asynchron auf den ThreadPool —
/// Assertions direkt nach dem await laufen dann in ein Race (auf CI-Runnern
/// unter Last regelmäßig verloren) und List.Add wäre nicht thread-sicher.
/// Diese Implementierung ruft den Handler inline auf: deterministisch,
/// kein Delay-Workaround nötig.
/// </summary>
public sealed class SyncProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public SyncProgress(Action<T> handler) => _handler = handler;

    public void Report(T value) => _handler(value);
}
