using System.Collections.Concurrent;

namespace DomainBlocks.EventSourcing;

public sealed class CompositeStateAdapterResolver<TEvent, TStreamId>(
    IEnumerable<IEventSourcedStateAdapterResolver<TEvent, TStreamId>> resolvers) :
    IEventSourcedStateAdapterResolver<TEvent, TStreamId>
    where TEvent : notnull
    where TStreamId : notnull
{
    private readonly ConcurrentDictionary<Type, IEventSourcedStateAdapter> _adapters = new();
    private readonly IEventSourcedStateAdapterResolver<TEvent, TStreamId>[] _resolvers = [.. resolvers];

    public IEventSourcedStateAdapter<TState, TEvent, TStreamId>? Resolve<TState>() where TState : notnull
    {
        if (_adapters.TryGetValue(typeof(TState), out var adapter))
            return (IEventSourcedStateAdapter<TState, TEvent, TStreamId>)adapter;

        var newAdapter = _resolvers.Select(x => x.Resolve<TState>()).FirstOrDefault(x => x != null);

        if (newAdapter != null)
            _adapters.TryAdd(typeof(TState), newAdapter);

        return newAdapter;
    }
}