namespace EasySave.RemoteConsole.Infrastructure;

// Minimal hot observable that replays the latest value to new subscribers.
// Avoids a dependency on System.Reactive while satisfying IObservable<T>.
internal sealed class BehaviorSubject<T> : IObservable<T>
{
    private readonly List<IObserver<T>> _observers = new();
    private readonly object _lock = new();
    private T _current;

    public BehaviorSubject(T initial) => _current = initial;

    public T Value { get { lock (_lock) return _current; } }

    public IDisposable Subscribe(IObserver<T> observer)
    {
        lock (_lock)
        {
            _observers.Add(observer);
            observer.OnNext(_current);
        }
        return new Subscription(() => { lock (_lock) _observers.Remove(observer); });
    }

    public void OnNext(T value)
    {
        IObserver<T>[] snapshot;
        lock (_lock)
        {
            _current = value;
            snapshot = _observers.ToArray();
        }
        foreach (var o in snapshot) o.OnNext(value);
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
