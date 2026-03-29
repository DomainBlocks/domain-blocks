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

    private void Detach(TObserver observer)
    {
        ImmutableInterlocked.Update(
            ref _observers,
            static (current, item) => current.Remove(item),
            observer);
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
}