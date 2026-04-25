using System.Windows.Threading;

namespace BcContainerLauncher.App.Services;

/// <summary>
/// <see cref="IProgress{T}"/>-Implementierung, die den Callback garantiert
/// auf dem WPF-Dispatcher ausführt. <see cref="Progress{T}"/> tut das nur,
/// wenn der SynchronizationContext beim Konstruktor schon der UI-Kontext war.
/// </summary>
public sealed class DispatcherProgress<T> : IProgress<T>
{
    private readonly Action<T> _callback;
    private readonly Dispatcher _dispatcher;

    public DispatcherProgress(Action<T> callback, Dispatcher? dispatcher = null)
    {
        _callback = callback;
        _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    public void Report(T value)
    {
        if (_dispatcher.CheckAccess())
        {
            _callback(value);
        }
        else
        {
            _dispatcher.BeginInvoke(_callback, DispatcherPriority.Background, value);
        }
    }
}
