namespace EasySave.RemoteConsole.Infrastructure;

// Minimal IObservable<T> / hot subject without an Rx dependency.
internal sealed class SimpleSubject<T> : IObservable<T>, IDisposable
{
    private readonly List<IObserver<T>> _observers = [];
    private readonly object _lock = new();
    private bool _completed;

    public IDisposable Subscribe(IObserver<T> observer)
    {
        lock (_lock)
        {
            if (!_completed)
                _observers.Add(observer);
        }
        return new Subscription(() => { lock (_lock) { _observers.Remove(observer); } });
    }

    public void OnNext(T value)
    {
        IObserver<T>[] snapshot;
        lock (_lock) { snapshot = [.. _observers]; }
        foreach (var obs in snapshot)
        {
            try { obs.OnNext(value); } catch { }
        }
    }

    public void OnCompleted()
    {
        IObserver<T>[] snapshot;
        lock (_lock)
        {
            _completed = true;
            snapshot = [.. _observers];
            _observers.Clear();
        }
        foreach (var obs in snapshot)
        {
            try { obs.OnCompleted(); } catch { }
        }
    }

    public void Dispose() => OnCompleted();

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        public void Dispose() => unsubscribe();
    }
}
