using System.Collections.Immutable;

namespace DomainBlocks.Infrastructure.MongoDB.ChangeStreams;

internal sealed class ObserverRegistry<TObserver>
{
    private ImmutableArray<TObserver> _observers = [];

    public IDisposable Attach(TObserver observer)
    {
        ImmutableInterlocked.Update(
            ref _observers,
            static (current, item) => current.Add(item),
            observer);

        return new ObserverAttachment(this, observer);
    }

    public IDisposable AttachGroup(IEnumerable<TObserver> observers)
    {
        var items = observers.ToArray();
        if (items.Length == 0)
            return EmptyDisposable.Instance;

        ImmutableInterlocked.Update(
            ref _observers,
            static (current, items) => current.AddRange(items),
            items);

        return new ObserverGroupAttachment(this, items);
    }

    private void Detach(TObserver observer)
    {
        ImmutableInterlocked.Update(
            ref _observers,
            static (current, item) => current.Remove(item),
            observer);
    }

    private void DetachGroup(TObserver[] observers)
    {
        ImmutableInterlocked.Update(
            ref _observers,
            static (current, items) => current.RemoveRange(items),
            observers);
    }

    public ImmutableArray<TObserver> Snapshot() => _observers;

    private sealed class ObserverAttachment(ObserverRegistry<TObserver> registry, TObserver observer) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                registry.Detach(observer);
        }
    }

    private sealed class ObserverGroupAttachment(ObserverRegistry<TObserver> registry, TObserver[] observers) :
        IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                registry.DetachGroup(observers);
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();

        private EmptyDisposable()
        {
        }

        public void Dispose()
        {
        }
    }
}